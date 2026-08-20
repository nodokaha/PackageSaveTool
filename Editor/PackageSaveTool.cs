using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography; // ★追加：MD5使用に必要
using System.Text;
using System.Text.RegularExpressions;
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
        private bool integrationMode = false;

        [MenuItem("Tools/Package Save Tool")]
        public static void ShowWindow()
        {
            GetWindow<PackageSaveTool>("Package Save Tool");
        }

        private void OnEnable()
        {
            LoadVersionInfo();
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
            {
                EditorPrefs.SetBool(IntegrationModeEditorPrefKey, integrationMode);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Increment Version", GUILayout.Height(35)))
            {
                ShowVersionIncrementDialog();
            }

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            if (GUILayout.Button("Save Folder (With Version Control & Dependencies)", GUILayout.Height(40)))
            {
                FolderSelectionWindow.ShowWindow(OnFoldersSelected);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Load Folder (Import & Fix References)", GUILayout.Height(40)))
            {
                // 1. ファイルの読み込みと配置
                LoadFolderWithReferenceFixing();

                // 2. アセットデータベースの強制更新
                AssetDatabase.Refresh();

                // 3. 参照補完・アニメーションコントローラーのMissing修復
                FixAllAnimatorControllers();

                // 4. 完了通知
                EditorUtility.DisplayDialog("Complete", "フォルダの読み込みと参照の自動補完が完了しました。", "OK");
            }

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            EditorGUILayout.Space();

            if (GUILayout.Button("Copy Components from Prefab to FBX", GUILayout.Height(35)))
            {
                CopyComponentsFromPrefabToFBX();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("1. Scan Missing Animation Clips", GUILayout.Height(30)))
            {
                ScanAllAnimatorControllers();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("2. Fix Animator Controllers Missing Clips", GUILayout.Height(30)))
            {
                FixAllAnimatorControllers();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Save: 選択された項目とDependencies(依存関係)を一括保存します。\n\n" +
                "Load: Manifestファイルを元に相対パスで正確に復元読み込みを行います。\n\n" +
                "Copy Components: Prefabのコンポーネント設定をFBXモデル階層に転写し、新たなPrefabを出力します。\n\n" +
                "Scan / Fix: AnimatorController内のMissingアニメーションクリップを検出・補完します。",
                MessageType.Info);

            GUILayout.EndScrollView();
        }

        #region バージョン管理
        private void ShowVersionIncrementDialog()
        {
            var buttons = new[] { "破壊的変更（メジャー版UP v1.0.0→v2.0.0）", "互換性あり（マイナー版UP v1.0.0→v1.1.0）", "キャンセル" };

            int result = EditorUtility.DisplayDialogComplex(
                "バージョン増加方法を選択",
                "このバージョンは破壊的な変更を含みますか？\n\n" +
                "【破壊的変更】\n既存のデータ/設定と互換性がない場合（メジャーバージョン UP）\n\n" +
                "【互換性あり】\n機能追加だが既存との互換性がある場合（マイナーバージョン UP）",
                buttons[0], buttons[2], buttons[1]
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

            SaveVersionInfo();
        }

        private void SaveVersionInfo()
        {
            string versionFile = Path.Combine(Application.persistentDataPath, "version_info.json");
            string jsonData = JsonUtility.ToJson(currentVersion, true);
            File.WriteAllText(versionFile, jsonData);
        }

        private void LoadVersionInfo()
        {
            string versionFile = Path.Combine(Application.persistentDataPath, "version_info.json");
            if (File.Exists(versionFile))
            {
                string jsonData = File.ReadAllText(versionFile);
                currentVersion = JsonUtility.FromJson<VersionInfo>(jsonData) ?? new VersionInfo(1, 0, 0);
            }
        }

        private VersionInfo ExtractVersionFromFolderName(string folderPath)
        {
            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var match = Regex.Match(folderName, @"[_=]([vV]?\d+\.\d+\.\d+)$");

            if (match.Success)
            {
                string versionStr = match.Groups[1].Value.ToLower();
                if (versionStr.StartsWith("v"))
                    versionStr = versionStr.Substring(1);

                var parts = versionStr.Split('.');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int major) &&
                    int.TryParse(parts[1], out int minor) &&
                    int.TryParse(parts[2], out int patch))
                {
                    return new VersionInfo(major, minor, patch);
                }
            }

            return null;
        }

        private void UpdateVersionAfterLoad(VersionInfo loadedVersion)
        {
            int comparison = CompareVersions(loadedVersion, currentVersion);

            if (comparison > 0)
            {
                currentVersion = loadedVersion;
                SaveVersionInfo();
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
                    currentVersion = loadedVersion;
                    SaveVersionInfo();
                    Debug.Log($"Version updated to {currentVersion} (user confirmed)");
                }
            }
        }

        private int CompareVersions(VersionInfo v1, VersionInfo v2)
        {
            if (v1.major != v2.major)
                return v1.major.CompareTo(v2.major);
            if (v1.minor != v2.minor)
                return v1.minor.CompareTo(v2.minor);
            return v1.patch.CompareTo(v2.patch);
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

            selectedPaths = FilterTopLevelSelections(selectedPaths);
            HashSet<string> targetAssetPaths = new HashSet<string>();

            foreach (var path in selectedPaths)
            {
                string assetPath = NormalizeToAssetPath(path);
                if (string.IsNullOrEmpty(assetPath)) continue;

                if (Directory.Exists(path))
                {
                    string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (file.EndsWith(".meta")) continue;
                        string fileAssetPath = NormalizeToAssetPath(file);
                        if (!string.IsNullOrEmpty(fileAssetPath))
                            targetAssetPaths.Add(fileAssetPath);
                    }
                }
                else if (File.Exists(path))
                {
                    targetAssetPaths.Add(assetPath);
                }
            }

            // 依存関係を全自動追加
            string[] dependencies = AssetDatabase.GetDependencies(targetAssetPaths.ToArray(), recursive: true);
            foreach (var dep in dependencies)
            {
                if (dep.StartsWith("Assets/"))
                {
                    targetAssetPaths.Add(dep);
                }
            }

            string sanitizedAuthor = SanitizeFileName(authorName);
            if (string.IsNullOrEmpty(sanitizedAuthor))
                sanitizedAuthor = "Unknown";

            string saveFolder = $"{sanitizedAuthor}_{currentVersion}";
            string savePath = Path.Combine(destinationPath, saveFolder);
            Directory.CreateDirectory(savePath);

            var manifestRelativePaths = new List<string>();

            foreach (var assetPath in targetAssetPaths)
            {
                string relPath = GetPathRelativeToAssets(assetPath);
                if (string.IsNullOrEmpty(relPath)) continue;

                string sysSourcePath = Path.Combine(Application.dataPath, relPath);
                string sysDestPath = Path.Combine(savePath, relPath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(sysSourcePath))
                {
                    string destDir = Path.GetDirectoryName(sysDestPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(sysSourcePath, sysDestPath, true);
                    manifestRelativePaths.Add(relPath);

                    string sysMetaSource = sysSourcePath + ".meta";
                    if (File.Exists(sysMetaSource))
                    {
                        File.Copy(sysMetaSource, sysDestPath + ".meta", true);
                    }
                }
            }

            var manifest = new SelectionManifest { relativePaths = manifestRelativePaths.Distinct().ToArray() };
            string manifestPath = Path.Combine(savePath, ManifestFileName);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));

            Debug.Log($"[PackageSaveTool] Saved successfully with dependencies to: {savePath}");

            currentVersion.IncrementPatch();
            SaveVersionInfo();
            Debug.Log($"Next Version: {currentVersion}");

            EditorUtility.RevealInFinder(savePath);
        }

        private void LoadFolderWithReferenceFixing()
        {
            // 1. 読み込み元フォルダを選択
            string folderPath = EditorUtility.OpenFolderPanel("Select Folder to Load", "", "");
            if (string.IsNullOrEmpty(folderPath))
            {
                Debug.Log("Load cancelled.");
                return;
            }

            // Manifestが存在する場合はManifest駆動読み込みを実行
            string manifestPath = Path.Combine(folderPath, ManifestFileName);
            if (File.Exists(manifestPath))
            {
                LoadUsingManifest(folderPath, manifestPath);
                return;
            }

            // --- Manifestが無い旧形式パッケージ向けの処理 ---

            // 2. インポート先を選択 (Assets内)
            string importPath = EditorUtility.SaveFolderPanel("Select Import Destination (in Assets folder)", "Assets", "");
            if (string.IsNullOrEmpty(importPath))
            {
                Debug.Log("Import destination cancelled.");
                return;
            }

            string sourceFolder = GetImportSourceFolder(folderPath);
            string finalImportPath = Path.Combine(importPath, new DirectoryInfo(sourceFolder).Name);

            // 既存フォルダが存在し、かつ差分が存在する場合に専用GUIウィンドウを表示
            if (Directory.Exists(finalImportPath) && FolderHasChanges(sourceFolder, finalImportPath))
            {
                // 詳細な差分情報（パラメータ差分含む）を取得
                DetailedFolderDiffInfo diffInfo = GetDetailedFolderDifferences(sourceFolder, finalImportPath);

                // 差分確認ウィンドウを開き、ユーザーが「実行」を押した場合のコールバックを指定
                DiffResultWindow.ShowWindow(diffInfo, sourceFolder, finalImportPath, () =>
                {
                    ExecuteLoad(sourceFolder, finalImportPath, folderPath);
                });

                return; // ウィンドウ側での操作待ちにするためここで一時中断
            }

            // 差分が無い場合はそのまま読み込みを実行
            ExecuteLoad(sourceFolder, finalImportPath, folderPath);
        }

        /// <summary>
        /// 差分確認完了後の実際のファイルコピーとバージョン更新処理
        /// </summary>
        private void ExecuteLoad(string sourceFolder, string finalImportPath, string originalFolderPath)
        {
            CopyFolder(sourceFolder, finalImportPath, integrationMode);

            VersionInfo loadedVersion = ExtractVersionFromFolderName(originalFolderPath);
            if (loadedVersion != null)
            {
                UpdateVersionAfterLoad(loadedVersion);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[PackageSaveTool] 読み込みが完了しました: {finalImportPath}");
        }

        private void LoadUsingManifest(string sourceRoot, string manifestPath)
                {
                    string json = File.ReadAllText(manifestPath);
                    SelectionManifest manifest = null;
                    try
                    {
                        manifest = JsonUtility.FromJson<SelectionManifest>(json);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Failed to parse manifest: {e.Message}");
                        return;
                    }

                    if (manifest == null || manifest.relativePaths == null || manifest.relativePaths.Length == 0)
                    {
                        Debug.LogWarning("Manifest is empty or invalid. Nothing to update.");
                        return;
                    }

                    string projectAssetsPath = Application.dataPath;

                    // マニフェストの範囲内に限定して詳細差分を取得
                    DetailedFolderDiffInfo diffInfo = GetManifestDetailedDifferences(sourceRoot, projectAssetsPath, manifest.relativePaths);

                    // 差分が存在する場合は確認ウィンドウを表示
                    if (diffInfo.FileDetails.Count > 0)
                    {
                        DiffResultWindow.ShowWindow(diffInfo, sourceRoot, projectAssetsPath, () =>
                        {
                            ExecuteLoadUsingManifest(sourceRoot, manifest);
                        });
                    }
                    else
                    {
                        // 差分が無い場合はそのまま読み込みを実行
                        ExecuteLoadUsingManifest(sourceRoot, manifest);
                    }
                }

                /// <summary>
                /// 差分確認完了後の実際のManifestベース読み込み処理
                /// </summary>
                private void ExecuteLoadUsingManifest(string sourceRoot, SelectionManifest manifest)
                {
                    string projectAssetsPath = Application.dataPath;

                    foreach (var relPath in manifest.relativePaths)
                    {
                        if (string.IsNullOrEmpty(relPath)) continue;

                        string normalizedRel = relPath.Replace('/', Path.DirectorySeparatorChar);
                        string sourcePath = Path.Combine(sourceRoot, normalizedRel);
                        string destPath = Path.Combine(projectAssetsPath, normalizedRel);

                        if (File.Exists(sourcePath))
                        {
                            string destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir))
                                Directory.CreateDirectory(destDir);

                            File.Copy(sourcePath, destPath, true);

                            if (File.Exists(sourcePath + ".meta"))
                            {
                                File.Copy(sourcePath + ".meta", destPath + ".meta", true);
                            }
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            SyncManifestFolderOnly(sourcePath, destPath);
                        }
                    }

                    VersionInfo loadedVersion = ExtractVersionFromFolderName(sourceRoot);
                    if (loadedVersion != null)
                    {
                        UpdateVersionAfterLoad(loadedVersion);
                    }

                    AssetDatabase.Refresh();
                    Debug.Log("[PackageSaveTool] Load completed using selection_manifest scope.");
                }

        /// <summary>
        /// マニフェストに定義されたファイル群のみを対象とした差分検出
        /// </summary>
        private DetailedFolderDiffInfo GetManifestDetailedDifferences(string sourceRoot, string projectAssetsPath, string[] relativePaths)
        {
            var result = new DetailedFolderDiffInfo();

            foreach (var relPath in relativePaths)
            {
                if (string.IsNullOrEmpty(relPath)) continue;

                string normalizedRel = relPath.Replace('/', Path.DirectorySeparatorChar);
                string srcPath = Path.Combine(sourceRoot, normalizedRel);
                string dstPath = Path.Combine(projectAssetsPath, normalizedRel);

                bool inSource = File.Exists(srcPath);
                bool inDest = File.Exists(dstPath);

                var fileDetail = new FileDiffDetail { RelativePath = relPath };

                if (inSource && !inDest)
                {
                    fileDetail.Status = "新規追加";
                    result.FileDetails.Add(fileDetail);
                }
                else if (!inSource && inDest)
                {
                    fileDetail.Status = "削除";
                    result.FileDetails.Add(fileDetail);
                }
                else if (inSource && inDest && !FileHashEquals(srcPath, dstPath))
                {
                    fileDetail.Status = "変更あり";

                    // YAML / アセットファイル等のパラメータ差分を取得
                    if (IsAssetFile(srcPath))
                    {
                        fileDetail.PropertyDiffs = CompareProperties(dstPath, srcPath);
                    }

                    result.FileDetails.Add(fileDetail);
                }
            }

            return result;
        }

        private void SyncManifestFolderOnly(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            if (!integrationMode)
            {
                var destFiles = Directory.GetFiles(destDir, "*.*", SearchOption.AllDirectories);
                foreach (var destFile in destFiles)
                {
                    string relToDest = destFile.Substring(destDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string correspondingSource = Path.Combine(sourceDir, relToDest);

                    if (!File.Exists(correspondingSource))
                    {
                        File.Delete(destFile);
                    }
                }
            }

            var sourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
            foreach (var srcFile in sourceFiles)
            {
                string relToSource = srcFile.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string targetDest = Path.Combine(destDir, relToSource);

                string targetDir = Path.GetDirectoryName(targetDest);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.Copy(srcFile, targetDest, true);
            }
        }
        #endregion

        #region FBXコンポーネントコピー機能
        private void CopyComponentsFromPrefabToFBX()
        {
            string sourcePrefabPath = EditorUtility.OpenFilePanel("Select Source Prefab", "Assets", "prefab");
            if (string.IsNullOrEmpty(sourcePrefabPath)) return;

            string relativeSourcePath = NormalizeToAssetPath(sourcePrefabPath);
            if (string.IsNullOrEmpty(relativeSourcePath))
            {
                EditorUtility.DisplayDialog("Error", "Selected prefab is not in Assets folder.", "OK");
                return;
            }

            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(relativeSourcePath);

            string targetFBXPath = EditorUtility.OpenFilePanel("Select Target FBX", "Assets", "fbx");
            if (string.IsNullOrEmpty(targetFBXPath)) return;

            string relativeTargetPath = NormalizeToAssetPath(targetFBXPath);
            if (string.IsNullOrEmpty(relativeTargetPath))
            {
                EditorUtility.DisplayDialog("Error", "Selected FBX is not in Assets folder.", "OK");
                return;
            }

            GameObject targetFBX = AssetDatabase.LoadAssetAtPath<GameObject>(relativeTargetPath);

            string savePath = EditorUtility.SaveFilePanel("Save FBX as Prefab", "Assets", Path.GetFileNameWithoutExtension(relativeTargetPath) + "_withComponents", "prefab");
            if (string.IsNullOrEmpty(savePath)) return;

            string relativeSavePath = NormalizeToAssetPath(savePath);

            GameObject instance = PrefabUtility.InstantiatePrefab(targetFBX) as GameObject;
            if (instance == null)
            {
                instance = Instantiate(targetFBX);
            }

            CopyComponents(sourcePrefab, instance, sourcePrefab, instance);

            PrefabUtility.SaveAsPrefabAsset(instance, relativeSavePath);
            DestroyImmediate(instance);

            AssetDatabase.Refresh();
            Debug.Log($"Successfully copied components to {relativeSavePath}");
            EditorUtility.RevealInFinder(savePath);
        }

        private void CopyComponents(GameObject source, GameObject target, GameObject sourceRoot, GameObject targetRoot)
        {
            Component[] sourceComponents = source.GetComponents<Component>();

            foreach (Component sourceComponent in sourceComponents)
            {
                if (sourceComponent is Transform)
                    continue;

                System.Type componentType = sourceComponent.GetType();
                Component existingComponent = target.GetComponent(componentType);

                if (existingComponent != null)
                {
                    CopyComponentProperties(sourceComponent, existingComponent, sourceRoot, targetRoot);
                }
                else
                {
                    Component newComponent = target.AddComponent(componentType);
                    CopyComponentProperties(sourceComponent, newComponent, sourceRoot, targetRoot);
                }
            }

            for (int i = 0; i < source.transform.childCount; i++)
            {
                Transform sourceChild = source.transform.GetChild(i);
                Transform targetChild = target.transform.Find(sourceChild.name);

                if (targetChild == null)
                {
                    GameObject newChild = new GameObject(sourceChild.name);
                    newChild.transform.SetParent(target.transform);
                    newChild.transform.localPosition = sourceChild.localPosition;
                    newChild.transform.localRotation = sourceChild.localRotation;
                    newChild.transform.localScale = sourceChild.localScale;
                    targetChild = newChild.transform;
                }

                CopyComponents(sourceChild.gameObject, targetChild.gameObject, sourceRoot, targetRoot);
            }
        }

        private void CopyComponentProperties(Component sourceComponent, Component targetComponent, GameObject sourceRoot, GameObject targetRoot)
        {
            SerializedObject sourceSO = new SerializedObject(sourceComponent);
            SerializedObject targetSO = new SerializedObject(targetComponent);

            SerializedProperty sourceProp = sourceSO.GetIterator();
            while (sourceProp.NextVisible(true))
            {
                if (sourceProp.propertyType == SerializedPropertyType.ObjectReference &&
                    (sourceProp.name == "m_Materials" || sourceProp.name == "bones" ||
                     sourceProp.name == "m_SharedMaterials" || sourceProp.name == "m_SharedMesh" ||
                     sourceProp.name == "m_Mesh"))
                {
                    continue;
                }

                SerializedProperty targetProp = targetSO.FindProperty(sourceProp.propertyPath);
                if (targetProp == null || targetProp.propertyType != sourceProp.propertyType)
                    continue;

                if (sourceProp.propertyType == SerializedPropertyType.ObjectReference && sourceProp.objectReferenceValue != null)
                {
                    Object resolvedRef = ResolveObjectReference(sourceProp.objectReferenceValue, sourceRoot, targetRoot);
                    if (resolvedRef != null)
                    {
                        targetProp.objectReferenceValue = resolvedRef;
                        targetSO.ApplyModifiedProperties();
                        continue;
                    }
                }

                targetSO.CopyFromSerializedProperty(sourceProp);
            }

            targetSO.ApplyModifiedProperties();
        }

        private Object ResolveObjectReference(Object sourceReference, GameObject sourceRoot, GameObject targetRoot)
        {
            if (sourceReference == null || sourceRoot == null || targetRoot == null)
                return sourceReference;

            if (sourceReference is Transform sourceTransform)
            {
                return FindCorrespondingTransform(sourceTransform, sourceRoot, targetRoot);
            }

            if (sourceReference is GameObject sourceGameObject)
            {
                var targetTransform = FindCorrespondingTransform(sourceGameObject.transform, sourceRoot, targetRoot);
                return targetTransform != null ? targetTransform.gameObject : null;
            }

            if (sourceReference is Component sourceComponent)
            {
                var targetTransform = FindCorrespondingTransform(sourceComponent.transform, sourceRoot, targetRoot);
                if (targetTransform == null)
                    return null;
                return targetTransform.GetComponent(sourceComponent.GetType());
            }

            return sourceReference;
        }

        private Transform FindCorrespondingTransform(Transform sourceTransform, GameObject sourceRoot, GameObject targetRoot)
        {
            string relativePath = GetRelativeTransformPath(sourceTransform, sourceRoot.transform);
            if (relativePath == null)
                return null;

            if (string.IsNullOrEmpty(relativePath))
                return targetRoot.transform;

            return targetRoot.transform.Find(relativePath);
        }

        private string GetRelativeTransformPath(Transform transform, Transform root)
        {
            if (transform == root)
                return string.Empty;

            var segments = new List<string>();
            Transform current = transform;
            while (current != null && current != root)
            {
                segments.Insert(0, current.name);
                current = current.parent;
            }

            return current == root ? string.Join("/", segments) : null;
        }
        #endregion

        #region Animator修復呼び出し
        private void ScanAllAnimatorControllers()
        {
            AssetDatabase.Refresh();
            string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
            var allReports = new List<AnimatorFixReport>();

            foreach (string guid in controllerGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller != null)
                {
                    var reports = AnimatorControllerFixer.ScanMissingClips(controller);
                    allReports.AddRange(reports);
                }
            }

            AnimatorFixResultWindow.ShowReport(allReports, "Animator Missing Scan Results");
        }

        private void FixAllAnimatorControllers()
        {
            AssetDatabase.Refresh();
            string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
            var allReports = new List<AnimatorFixReport>();

            foreach (string guid in controllerGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller != null)
                {
                    var reports = AnimatorControllerFixer.DetectAndFixMissingClips(controller);
                    allReports.AddRange(reports);
                }
            }

            AnimatorFixResultWindow.ShowReport(allReports, "Animator Fix Results");
        }
        #endregion

        #region ユーティリティ
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder();
            foreach (var c in fileName)
            {
                if (!invalidChars.Contains(c))
                    sanitized.Append(c);
            }
            return sanitized.ToString().Trim();
        }

        private string GetPathRelativeToAssets(string fullPath)
        {
            string normalized = fullPath.Replace('\\', '/').TrimEnd('/');
            string assetsAbsolute = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (normalized.StartsWith(assetsAbsolute + "/", System.StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(assetsAbsolute.Length + 1);

            if (normalized.Equals(assetsAbsolute, System.StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            const string prefix = "Assets/";
            if (normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(prefix.Length);

            if (normalized.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return Path.GetFileName(normalized);
        }

        private string NormalizeToAssetPath(string path)
        {
            string normalized = path.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (normalized.StartsWith(dataPath))
            {
                return "Assets" + normalized.Substring(dataPath.Length);
            }
            if (normalized.StartsWith("Assets/"))
            {
                return normalized;
            }
            return null;
        }

        private List<string> FilterTopLevelSelections(List<string> selectedPaths)
        {
            var filtered = new List<string>();
            var normalizedPaths = selectedPaths.Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();

            foreach (var path in normalizedPaths.OrderBy(p => p.Length))
            {
                if (!filtered.Any(parent => IsAncestor(parent, path)))
                {
                    filtered.Add(path);
                }
            }

            return filtered;
        }

        private bool IsAncestor(string ancestorPath, string path)
        {
            if (string.Equals(ancestorPath, path, System.StringComparison.OrdinalIgnoreCase))
                return false;

            ancestorPath = ancestorPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(ancestorPath, System.StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region 差分判定ロジック
        public static DetailedFolderDiffInfo GetDetailedFolderDifferences(string sourceFolder, string destinationFolder)
        {
            var result = new DetailedFolderDiffInfo();
            var sourceFiles = GetAllFiles(sourceFolder).ToDictionary(f => GetRelativePath(f, sourceFolder));
            var destFiles = GetAllFiles(destinationFolder).ToDictionary(f => GetRelativePath(f, destinationFolder));

            var allRelPaths = sourceFiles.Keys.Union(destFiles.Keys).OrderBy(p => p);

            foreach (var relPath in allRelPaths)
            {
                bool inSource = sourceFiles.TryGetValue(relPath, out string srcPath);
                bool inDest = destFiles.TryGetValue(relPath, out string dstPath);

                var fileDetail = new FileDiffDetail { RelativePath = relPath };

                if (inSource && !inDest)
                {
                    fileDetail.Status = "新規追加";
                    result.FileDetails.Add(fileDetail);
                }
                else if (!inSource && inDest)
                {
                    fileDetail.Status = "削除";
                    result.FileDetails.Add(fileDetail);
                }
                else if (inSource && inDest && !FileHashEquals(srcPath, dstPath))
                {
                    fileDetail.Status = "変更あり";

                    // YAML/アセットファイル等のパラメータ差分を取得
                    if (IsAssetFile(srcPath))
                    {
                        fileDetail.PropertyDiffs = CompareProperties(dstPath, srcPath);
                    }

                    result.FileDetails.Add(fileDetail);
                }
            }

            return result;
        }

        /// <summary>
        /// アセットファイル内のキー＆バリュー形式の行を比較してパラメータ差分を抽出
        /// </summary>
        private static List<PropertyDiffItem> CompareProperties(string oldFilePath, string newFilePath)
        {
            var oldProps = ParseYamlProperties(oldFilePath);
            var newProps = ParseYamlProperties(newFilePath);
            var diffs = new List<PropertyDiffItem>();

            // 変更および新規キーの抽出
            foreach (var kv in newProps)
            {
                if (oldProps.TryGetValue(kv.Key, out string oldValue))
                {
                    if (oldValue != kv.Value)
                    {
                        diffs.Add(new PropertyDiffItem
                        {
                            PropertyName = kv.Key,
                            OldValue = oldValue,
                            NewValue = kv.Value
                        });
                    }
                }
                else
                {
                    diffs.Add(new PropertyDiffItem
                    {
                        PropertyName = kv.Key,
                        OldValue = "(なし)",
                        NewValue = kv.Value
                    });
                }
            }

            // 削除されたキーの抽出
            foreach (var kv in oldProps)
            {
                if (!newProps.ContainsKey(kv.Key))
                {
                    diffs.Add(new PropertyDiffItem
                    {
                        PropertyName = kv.Key,
                        OldValue = kv.Value,
                        NewValue = "(削除)"
                    });
                }
            }

            return diffs;
        }

        /// <summary>
        /// YAML / Unityアセット形式のテキストからパラメータ(Key: Value)を抽出
        /// </summary>
        private static Dictionary<string, string> ParseYamlProperties(string filePath)
        {
            var props = new Dictionary<string, string>();
            string currentContext = "";

            foreach (var line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();

                // YAMLブロック/オブジェクトIDの判定
                if (trimmed.StartsWith("--- !u!"))
                {
                    currentContext = trimmed;
                    continue;
                }

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = trimmed.Substring(0, colonIndex).Trim();
                    string val = trimmed.Substring(colonIndex + 1).Trim();

                    // 値が存在し、コメント等でない場合にパース
                    if (!string.IsNullOrEmpty(val) && !key.StartsWith("#"))
                    {
                        string fullKey = string.IsNullOrEmpty(currentContext) ? key : $"{currentContext} -> {key}";
                        props[fullKey] = val;
                    }
                }
            }

            return props;
        }

        private static bool IsAssetFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            return ext == ".mat" || ext == ".prefab" || ext == ".asset" || ext == ".json" || ext == ".controller";
        }

        public static bool FileHashEquals(string file1, string file2)
        {
            var info1 = new FileInfo(file1);
            var info2 = new FileInfo(file2);

            // ファイルサイズが異なればハッシュ計算せずに偽を返す（高速化）
            if (info1.Length != info2.Length)
                return false;

            using (var md5 = MD5.Create())
            using (var stream1 = File.OpenRead(file1))
            using (var stream2 = File.OpenRead(file2))
            {
                byte[] hash1 = md5.ComputeHash(stream1);
                byte[] hash2 = md5.ComputeHash(stream2);
                return hash1.SequenceEqual(hash2);
            }
        }

        private static string GetRelativePath(string filePath, string rootFolder)
        {
            string normalizedFile = filePath.Replace('\\', '/');
            string normalizedRoot = rootFolder.Replace('\\', '/').TrimEnd('/') + '/';

            if (normalizedFile.StartsWith(normalizedRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile.Substring(normalizedRoot.Length);
            }

            return Path.GetFileName(filePath);
        }

        private static List<string> GetAllFiles(string folderPath)
        {
            return Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta"))
                .ToList();
        }
        #endregion

        #region ユーティリティ
                private string GetImportSourceFolder(string folderPath)
                {
                    string folderName = Path.GetFileName(folderPath);
                    if (IsVersionedWrapperFolder(folderName))
                    {
                        var childDirs = Directory.GetDirectories(folderPath);
                        if (childDirs.Length > 0)
                        {
                            return childDirs[0];
                        }
                    }
                    return folderPath;
                }

                private bool IsVersionedWrapperFolder(string folderName)
                {
                    if (string.IsNullOrEmpty(folderName)) return false;
                    return Regex.IsMatch(folderName, @"^.+(?:[_=])[vV]?\d+\.\d+\.\d+$");
                }

                private bool FolderHasChanges(string sourceFolder, string destinationFolder)
                {
                    if (!Directory.Exists(destinationFolder)) return true;

                    var sourceFiles = GetAllFiles(sourceFolder);
                    var destFiles = GetAllFiles(destinationFolder);

                    if (sourceFiles.Count != destFiles.Count) return true;

                    foreach (var file in sourceFiles)
                    {
                        string relativePath = GetRelativePath(file, sourceFolder);
                        string destFile = Path.Combine(destinationFolder, relativePath);

                        if (!File.Exists(destFile) || !FileHashEquals(file, destFile))
                            return true;
                    }

                    return false;
                }

        private void CopyFolder(string sourcePath, string destPath, bool merge)
        {
            if (!merge && Directory.Exists(destPath))
            {
                Directory.Delete(destPath, true);
            }

            Directory.CreateDirectory(destPath);

            foreach (var file in Directory.GetFiles(sourcePath))
            {
                string fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(destPath, fileName), true);
            }

            foreach (var folder in Directory.GetDirectories(sourcePath))
            {
                string folderName = Path.GetFileName(folder);
                CopyFolder(folder, Path.Combine(destPath, folderName), merge);
            }
        }
        #endregion

    }
}
