using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PackageSaveTool
{
    public class PackageSaveTool : EditorWindow
    {
        private const string AuthorEditorPrefKey = "PackageSaveTool_AuthorName";
        private const string IntegrationModeEditorPrefKey = "PackageSaveTool_IntegrationMode";
        private const string ManifestFileName = "_selection_manifest.json";

        private Vector2 scrollPosition;
        private VersionInfo currentVersion = new VersionInfo(1, 0, 0);
        private string authorName = "Unknown";
        private bool integrationMode;

        [MenuItem("Tools/Package Save Tool")]
        public static void ShowWindow()
        {
            GetWindow<PackageSaveTool>("Package Save Tool");
        }

        private void OnEnable()
        {
            currentVersion = PackageVersionStore.Load();
            authorName = EditorPrefs.GetString(AuthorEditorPrefKey, "Unknown");
            if (string.IsNullOrWhiteSpace(authorName))
                authorName = "Unknown";
            integrationMode = EditorPrefs.GetBool(IntegrationModeEditorPrefKey, false);
        }

        private void OnGUI()
        {
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Folder Management", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            GUILayout.Label("Current Version", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"Version: {currentVersion}", MessageType.Info);

            EditorGUILayout.Space();

            GUILayout.Label("Author", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            authorName = EditorGUILayout.TextField(authorName);
            if (EditorGUI.EndChangeCheck())
            {
                authorName = authorName.Trim();
                if (string.IsNullOrEmpty(authorName))
                    authorName = "Unknown";
                EditorPrefs.SetString(AuthorEditorPrefKey, authorName);
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            integrationMode = EditorGUILayout.ToggleLeft(
                "統合モード（マージ：既存フォルダを削除せず、追加・上書きのみ行う）",
                integrationMode);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(IntegrationModeEditorPrefKey, integrationMode);

            EditorGUILayout.Space();

            if (GUILayout.Button("Increment Version", GUILayout.Height(35)))
                ShowVersionIncrementDialog();

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            if (GUILayout.Button("Save Folder (With Version Control & Dependencies)", GUILayout.Height(40)))
                EditorApplication.delayCall += () => FolderSelectionWindow.ShowWindow(OnFoldersSelected);

            EditorGUILayout.Space();

            if (GUILayout.Button("Load Folder (Import & Fix References)", GUILayout.Height(40)))
                EditorApplication.delayCall += LoadFolderWithReferenceFixing;

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            if (GUILayout.Button("Copy Components from Prefab to FBX", GUILayout.Height(35)))
                CopyComponentsFromPrefabToFBX();

            EditorGUILayout.Space();

            if (GUILayout.Button("1. Scan Missing Animation Clips", GUILayout.Height(30)))
                ScanAnimatorControllers();

            EditorGUILayout.Space();

            if (GUILayout.Button("2. Fix Animator Controllers Missing Clips", GUILayout.Height(30)))
                FixAnimatorControllers();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Save: 選択された項目とDependencies(依存関係)を一括保存します。\n\n" +
                "Load: Manifestファイルを元に相対パスで正確に復元読み込みを行います。差分を表示し、ユーザー承認後にのみ読み込みます。\n\n" +
                "Copy Components: Prefabのコンポーネント設定をFBXモデル階層に転写し、新たなPrefabを出力します。\n\n" +
                "Scan / Fix: AnimatorController内のMissingアニメーションクリップを検出・補完します。Load後はスキャンのみ行い、修復は「2. Fix Animator Controllers Missing Clips」で実行します。",
                MessageType.Info);

            GUILayout.EndScrollView();
        }

        #region バージョン管理
        private void ShowVersionIncrementDialog()
        {
            int result = EditorUtility.DisplayDialogComplex(
                "バージョン増加方法を選択",
                "このバージョンは破壊的な変更を含みますか？\n\n" +
                "【破壊的変更】\n既存のデータ/設定と互換性がない場合（メジャーバージョン UP）\n\n" +
                "【互換性あり】\n機能追加だが既存との互換性がある場合（マイナーバージョン UP）",
                "破壊的変更（メジャー版UP v1.0.0→v2.0.0）",
                "キャンセル",
                "互換性あり（マイナー版UP v1.0.0→v1.1.0）"
            );

            if (result == 0)
            {
                currentVersion.IncrementMajor();
                Debug.Log($"Version incremented to {currentVersion} (Breaking Change - Major)");
            }
            else if (result == 2)
            {
                currentVersion.IncrementMinor();
                Debug.Log($"Version incremented to {currentVersion} (Compatible - Minor)");
            }
            else
            {
                return;
            }

            PackageVersionStore.Save(currentVersion);
        }

        private void UpdateVersionAfterLoad(VersionInfo loadedVersion)
        {
            int comparison = loadedVersion.CompareTo(currentVersion);

            if (comparison > 0)
            {
                currentVersion = loadedVersion.Clone();
                PackageVersionStore.Save(currentVersion);
                Debug.Log($"Version updated to {currentVersion} (loaded version is newer)");
            }
            else if (comparison < 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"警告: 読み込むバージョン {loadedVersion} は現在のバージョン {currentVersion} より古いです。");
                message.AppendLine();
                message.AppendLine("読み込んだバージョンを現在のバージョンに設定しますか？");

                if (EditorUtility.DisplayDialog("Version Warning", message.ToString(), "設定する", "キャンセル"))
                {
                    currentVersion = loadedVersion.Clone();
                    PackageVersionStore.Save(currentVersion);
                    Debug.Log($"Version updated to {currentVersion} (user confirmed)");
                }
            }
        }
        #endregion

        #region 保存 / 読み込み機能
        private void OnFoldersSelected(List<string> selectedPaths)
        {
            string destinationPath = EditorUtility.OpenFolderPanel("Select Save Destination", "", "");
            if (string.IsNullOrEmpty(destinationPath))
            {
                Debug.Log("Destination selection cancelled.");
                return;
            }

            try
            {
                selectedPaths = PathUtil.FilterTopLevelSelections(selectedPaths);
                var targetAssetPaths = CollectTargetAssetPaths(selectedPaths);
                if (targetAssetPaths.Count == 0)
                {
                    EditorUtility.DisplayDialog("Save", "保存対象のアセットが見つかりませんでした。", "OK");
                    return;
                }

                string sanitizedAuthor = PathUtil.SanitizeFileName(authorName);
                if (string.IsNullOrEmpty(sanitizedAuthor))
                    sanitizedAuthor = "Unknown";

                string saveFolder = $"{sanitizedAuthor}_{currentVersion}";
                string savePath = Path.Combine(destinationPath, saveFolder);
                var manifestRelativePaths = GetPackageRelativePaths(targetAssetPaths);
                var diffInfo = GetSavePackageDifferences(savePath, manifestRelativePaths);

                if (diffInfo.FileDetails.Count > 0 &&
                    !DiffResultWindow.Confirm(diffInfo, Application.dataPath, savePath, "保存を実行（上書き）"))
                {
                    return;
                }

                ExecuteSaveSelectedPackage(savePath, targetAssetPaths, manifestRelativePaths);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Save Failed", e.Message, "OK");
            }
        }

        private HashSet<string> CollectTargetAssetPaths(List<string> selectedPaths)
        {
            var targetAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in selectedPaths)
            {
                string assetPath = PathUtil.NormalizeToAssetPath(path);
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string fileAssetPath = PathUtil.NormalizeToAssetPath(file);
                        if (!string.IsNullOrEmpty(fileAssetPath))
                            targetAssetPaths.Add(fileAssetPath);
                    }
                }
                else if (File.Exists(path))
                {
                    targetAssetPaths.Add(assetPath);
                }
            }

            string[] dependencies = AssetDatabase.GetDependencies(targetAssetPaths.OrderBy(p => p).ToArray(), true);
            foreach (var dep in dependencies)
            {
                if (dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    targetAssetPaths.Add(dep);
            }

            return targetAssetPaths;
        }

        private static List<string> GetPackageRelativePaths(IEnumerable<string> targetAssetPaths)
        {
            return targetAssetPaths
                .Select(PathUtil.GetPathRelativeToAssets)
                .Where(relPath => !string.IsNullOrEmpty(relPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(relPath => relPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DetailedFolderDiffInfo GetSavePackageDifferences(string savePath, IEnumerable<string> manifestRelativePaths)
        {
            var relativeList = manifestRelativePaths.ToList();
            var pairs = new List<DiffFilePair>();
            var manifestSet = new HashSet<string>(relativeList, StringComparer.OrdinalIgnoreCase);

            foreach (var relPath in relativeList)
            {
                string sysSourcePath = PathUtil.CombineUnder(Application.dataPath, relPath);
                string sysDestPath = PathUtil.CombineUnder(savePath, relPath);
                pairs.Add(new DiffFilePair(relPath, sysSourcePath, sysDestPath));
            }

            if (Directory.Exists(savePath))
            {
                foreach (var destFile in FolderDiffer.GetAllFiles(savePath))
                {
                    string rel = PathUtil.GetRelativePath(destFile, savePath);
                    if (string.Equals(rel, ManifestFileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (manifestSet.Contains(rel))
                        continue;

                    pairs.Add(new DiffFilePair(rel, null, destFile));
                }
            }

            return FolderDiffer.ComparePairs(pairs, includeDeletes: true);
        }

        private void ExecuteSaveSelectedPackage(
            string savePath,
            IEnumerable<string> targetAssetPaths,
            IReadOnlyList<string> manifestRelativePaths)
        {
            Directory.CreateDirectory(savePath);

            var assetList = targetAssetPaths.ToList();
            using (var progress = new EditorProgress("Saving package"))
            {
                for (int i = 0; i < assetList.Count; i++)
                {
                    string assetPath = assetList[i];
                    string relPath = PathUtil.GetPathRelativeToAssets(assetPath);
                    if (string.IsNullOrEmpty(relPath))
                        continue;

                    progress.Report(relPath, (i + 1) / (float)assetList.Count);

                    string sysSourcePath = PathUtil.CombineUnder(Application.dataPath, relPath);
                    string sysDestPath = PathUtil.CombineUnder(savePath, relPath);
                    if (File.Exists(sysSourcePath))
                        FileSync.CopyFileWithMeta(sysSourcePath, sysDestPath);
                }
            }

            var manifest = new SelectionManifest { relativePaths = manifestRelativePaths.ToArray() };
            File.WriteAllText(Path.Combine(savePath, ManifestFileName), JsonUtility.ToJson(manifest, true));
            FileSync.RemoveOrphanFiles(savePath, manifestRelativePaths, ManifestFileName);

            currentVersion.IncrementPatch();
            PackageVersionStore.Save(currentVersion);

            Debug.Log($"[PackageSaveTool] Saved successfully with dependencies to: {savePath}");
            Debug.Log($"Next Version: {currentVersion}");
            EditorUtility.RevealInFinder(savePath);
        }

        private void LoadFolderWithReferenceFixing()
        {
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Load", "", "");
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.Log("Load cancelled.");
                return;
            }

            string manifestPath = Path.Combine(folderPath, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                LoadUsingManifest(folderPath, manifestPath);
                return;
            }

            string importPath = EditorUtility.SaveFolderPanel("Select Import Destination (in Assets folder)", "Assets", "");
            if (string.IsNullOrEmpty(importPath))
            {
                Debug.Log("Import destination cancelled.");
                return;
            }

            if (PathUtil.NormalizeToAssetPath(importPath) == null)
            {
                EditorUtility.DisplayDialog("Error", "Import destination must be inside the Assets folder.", "OK");
                return;
            }

            try
            {
                string sourceFolder = PackageVersionStore.GetImportSourceFolder(folderPath);
                string finalImportPath = Path.Combine(importPath, new DirectoryInfo(sourceFolder).Name);
                var diffInfo = FolderDiffer.CompareFolders(sourceFolder, finalImportPath, includeDeletes: !integrationMode);

                if (diffInfo.FileDetails.Count > 0 &&
                    !DiffResultWindow.Confirm(diffInfo, sourceFolder, finalImportPath, "読み込みを実行（上書き/統合）"))
                {
                    return;
                }

                ExecuteLoad(sourceFolder, finalImportPath, folderPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Load Failed", e.Message, "OK");
            }
        }

        private void ExecuteLoad(string sourceFolder, string finalImportPath, string originalFolderPath)
        {
            using (new AssetEditingScope())
            {
                FileSync.CopyDirectory(sourceFolder, finalImportPath, integrationMode);
            }

            VersionInfo loadedVersion = PackageVersionStore.ExtractFromFolderName(originalFolderPath);
            if (loadedVersion != null)
                UpdateVersionAfterLoad(loadedVersion);

            AssetDatabase.Refresh();
            Debug.Log($"[PackageSaveTool] 読み込みが完了しました: {finalImportPath}");

            var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectImportedAssetPaths(finalImportPath, imported);
            CompleteLoadAndScanAnimator(imported);
        }

        private void LoadUsingManifest(string sourceRoot, string manifestPath)
        {
            SelectionManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<SelectionManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse manifest: {e.Message}");
                EditorUtility.DisplayDialog("Load Failed", "マニフェストの解析に失敗しました。", "OK");
                return;
            }

            if (manifest == null || manifest.relativePaths == null || manifest.relativePaths.Length == 0)
            {
                Debug.LogWarning("Manifest is empty or invalid. Nothing to update.");
                EditorUtility.DisplayDialog("Load", "マニフェストが空です。", "OK");
                return;
            }

            try
            {
                var diffInfo = GetManifestDetailedDifferences(sourceRoot, Application.dataPath, manifest.relativePaths);
                if (diffInfo.FileDetails.Count > 0 &&
                    !DiffResultWindow.Confirm(diffInfo, sourceRoot, Application.dataPath, "読み込みを実行（上書き/統合）"))
                {
                    return;
                }

                ExecuteLoadUsingManifest(sourceRoot, manifest);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Load Failed", e.Message, "OK");
            }
        }

        private void ExecuteLoadUsingManifest(string sourceRoot, SelectionManifest manifest)
        {
            string projectAssetsPath = Application.dataPath;
            var importedAssetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (new AssetEditingScope())
            using (var progress = new EditorProgress("Loading package"))
            {
                for (int i = 0; i < manifest.relativePaths.Length; i++)
                {
                    string relPath = manifest.relativePaths[i];
                    if (string.IsNullOrEmpty(relPath))
                        continue;

                    progress.Report(relPath, (i + 1) / (float)manifest.relativePaths.Length);

                    if (!PathUtil.TryGetSafeManifestPaths(sourceRoot, projectAssetsPath, relPath, out string sourcePath, out string destPath))
                    {
                        Debug.LogWarning($"Skipped unsafe manifest path: {relPath}");
                        continue;
                    }

                    if (File.Exists(sourcePath))
                    {
                        FileSync.CopyFileWithMeta(sourcePath, destPath);
                        importedAssetPaths.Add(PathUtil.ToAssetPathFromRelative(relPath));
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        FileSync.CopyDirectory(sourcePath, destPath, integrationMode);
                        CollectImportedAssetPaths(destPath, importedAssetPaths);
                    }
                    else if (!integrationMode && File.Exists(destPath))
                    {
                        FileSync.DeleteFileWithMeta(destPath);
                    }
                }
            }

            VersionInfo loadedVersion = PackageVersionStore.ExtractFromFolderName(sourceRoot);
            if (loadedVersion != null)
                UpdateVersionAfterLoad(loadedVersion);

            AssetDatabase.Refresh();
            Debug.Log("[PackageSaveTool] Load completed using selection_manifest scope.");
            CompleteLoadAndScanAnimator(importedAssetPaths);
        }

        private static void CollectImportedAssetPaths(string folderPath, HashSet<string> importedAssetPaths)
        {
            foreach (var file in FolderDiffer.GetAllFiles(folderPath))
            {
                string assetPath = PathUtil.NormalizeToAssetPath(file);
                if (!string.IsNullOrEmpty(assetPath))
                    importedAssetPaths.Add(assetPath);
            }
        }

        private void CompleteLoadAndScanAnimator(ICollection<string> importedAssetPaths)
        {
            var reports = ScanAnimatorControllers(importedAssetPaths, showWindow: true);
            int missingCount = reports.Count;
            if (missingCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "Complete",
                    "フォルダの読み込みが完了しました。\nAnimationClip の Missing は見つかりませんでした。",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Animation Clips Missing",
                $"フォルダの読み込みが完了しました。\n\nAnimatorController 内に Missing AnimationClip が {missingCount} 件見つかりました。\n結果ウィンドウを確認できます。\n\n修復は自動では行いません。「2. Fix Animator Controllers Missing Clips」から実行してください。",
                "OK");
        }

        private DetailedFolderDiffInfo GetManifestDetailedDifferences(
            string sourceRoot,
            string projectAssetsPath,
            string[] relativePaths)
        {
            var pairs = new List<DiffFilePair>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expanded = new List<DiffFilePair>();

            foreach (var relPath in relativePaths)
            {
                if (string.IsNullOrEmpty(relPath))
                    continue;

                if (!PathUtil.TryGetSafeManifestPaths(sourceRoot, projectAssetsPath, relPath, out string srcPath, out string dstPath))
                {
                    Debug.LogWarning($"Skipped unsafe manifest path: {relPath}");
                    continue;
                }

                expanded.Clear();
                FolderDiffer.AddExpandedPairs(expanded, relPath, srcPath, dstPath, ManifestFileName);
                foreach (var pair in expanded)
                {
                    if (seen.Add(pair.RelativePath))
                        pairs.Add(pair);
                }
            }

            return FolderDiffer.ComparePairs(pairs, includeDeletes: !integrationMode);
        }
        #endregion

        #region FBXコンポーネントコピー機能
        private void CopyComponentsFromPrefabToFBX()
        {
            string sourcePrefabPath = EditorUtility.OpenFilePanel("Select Source Prefab", "Assets", "prefab");
            if (string.IsNullOrEmpty(sourcePrefabPath))
                return;

            string relativeSourcePath = PathUtil.NormalizeToAssetPath(sourcePrefabPath);
            if (string.IsNullOrEmpty(relativeSourcePath))
            {
                EditorUtility.DisplayDialog("Error", "Selected prefab is not in Assets folder.", "OK");
                return;
            }

            string targetFBXPath = EditorUtility.OpenFilePanel("Select Target FBX", "Assets", "fbx");
            if (string.IsNullOrEmpty(targetFBXPath))
                return;

            string relativeTargetPath = PathUtil.NormalizeToAssetPath(targetFBXPath);
            if (string.IsNullOrEmpty(relativeTargetPath))
            {
                EditorUtility.DisplayDialog("Error", "Selected FBX is not in Assets folder.", "OK");
                return;
            }

            string savePath = EditorUtility.SaveFilePanel(
                "Save FBX as Prefab",
                "Assets",
                Path.GetFileNameWithoutExtension(relativeTargetPath) + "_withComponents",
                "prefab");
            if (string.IsNullOrEmpty(savePath))
                return;

            string relativeSavePath = PathUtil.NormalizeToAssetPath(savePath);
            if (string.IsNullOrEmpty(relativeSavePath))
            {
                EditorUtility.DisplayDialog("Error", "Save path must be inside the Assets folder.", "OK");
                return;
            }

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(relativeSourcePath);
            GameObject targetFBX = AssetDatabase.LoadAssetAtPath<GameObject>(relativeTargetPath);

            if (!PrefabComponentCopier.TryCopy(sourcePrefab, targetFBX, relativeSavePath, out string error))
            {
                EditorUtility.DisplayDialog("Error", error ?? "Failed to copy components.", "OK");
                return;
            }

            Debug.Log($"Successfully copied components to {relativeSavePath}");
            EditorUtility.RevealInFinder(savePath);
        }
        #endregion

        #region Animator修復呼び出し
        private List<AnimatorFixReport> ScanAnimatorControllers(
            ICollection<string> assetPathFilter = null,
            bool showWindow = true)
        {
            var allReports = ProcessAnimatorControllers(
                controller => AnimatorControllerFixer.ScanMissingClips(controller),
                assetPathFilter,
                saveAssets: false);

            if (showWindow)
                AnimatorFixResultWindow.ShowReport(allReports, "Animator Missing Scan Results");

            return allReports;
        }

        private void FixAnimatorControllers(ICollection<string> assetPathFilter = null)
        {
            var allReports = ProcessAnimatorControllers(
                controller => AnimatorControllerFixer.FixMissingClips(controller),
                assetPathFilter,
                saveAssets: true);

            AnimatorFixResultWindow.ShowReport(allReports, "Animator Fix Results");
        }

        private static List<AnimatorFixReport> ProcessAnimatorControllers(
            Func<AnimatorController, List<AnimatorFixReport>> processor,
            ICollection<string> assetPathFilter,
            bool saveAssets)
        {
            var allReports = new List<AnimatorFixReport>();
            string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
            HashSet<string> filter = assetPathFilter != null
                ? new HashSet<string>(assetPathFilter, StringComparer.OrdinalIgnoreCase)
                : null;

            try
            {
                using (var progress = new EditorProgress(saveAssets ? "Fixing AnimatorControllers" : "Scanning AnimatorControllers"))
                {
                    for (int i = 0; i < controllerGuids.Length; i++)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(controllerGuids[i]);
                        progress.Report(path, (i + 1) / (float)controllerGuids.Length);

                        if (!PassesAssetFilter(path, filter))
                            continue;

                        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                        if (controller == null)
                            continue;

                        allReports.AddRange(processor(controller));
                    }
                }

                if (saveAssets)
                    AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Animator Tool Failed", e.Message, "OK");
            }

            return allReports;
        }

        private static bool PassesAssetFilter(string assetPath, HashSet<string> filter)
        {
            if (filter == null)
                return true;
            if (filter.Contains(assetPath))
                return true;

            foreach (var entry in filter)
            {
                if (string.IsNullOrEmpty(entry))
                    continue;

                string prefix = entry.TrimEnd('/') + "/";
                if (assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        #endregion
    }
}
