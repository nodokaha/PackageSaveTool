using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.IMGUI.Controls;

/// <summary>
/// セマンティックバージョン情報
/// </summary>
[System.Serializable]
public class VersionInfo
{
    public int major = 1;
    public int minor = 0;
    public int patch = 0;

    public VersionInfo() { }

    public VersionInfo(int major, int minor, int patch)
    {
        this.major = major;
        this.minor = minor;
        this.patch = patch;
    }

    public override string ToString()
    {
        return $"v{major}.{minor}.{patch}";
    }

    public void IncrementMajor()
    {
        major++;
        minor = 0;
        patch = 0;
    }

    public void IncrementMinor()
    {
        minor++;
        patch = 0;
    }

    public void IncrementPatch()
    {
        patch++;
    }
}

/// <summary>
/// ツリービュー用のアイテム（チェックボックス付き）
/// </summary>
public class FolderTreeItem : TreeViewItem
{
    public bool isChecked = false;
    public string fullPath = "";
    public bool isFolder = false;

    public FolderTreeItem(int id, int depth, string displayName, string fullPath, bool isFolder)
        : base(id, depth, displayName)
    {
        this.fullPath = fullPath;
        this.isFolder = isFolder;
    }
}

/// <summary>
/// フォルダ選択用ツリービュー
/// </summary>
public class FolderSelectionTreeView : TreeView
{
    private Dictionary<int, FolderTreeItem> itemDict = new Dictionary<int, FolderTreeItem>();
    private int nextId = 1;

    public FolderSelectionTreeView(TreeViewState state) : base(state)
    {
        Reload();
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
        itemDict.Clear();
        nextId = 1;

        // Assetsフォルダを起点に構築
        string assetsPath = "Assets";
        if (Directory.Exists(assetsPath))
        {
            BuildTreeRecursive(assetsPath, root, 0);
        }

        return root;
    }

    private void BuildTreeRecursive(string folderPath, TreeViewItem parent, int depth)
    {
        try
        {
            var item = new FolderTreeItem(nextId, depth, Path.GetFileName(folderPath), folderPath, true);
            itemDict[nextId] = item;
            parent.AddChild(item);
            int parentId = nextId;
            nextId++;

            // サブフォルダを追加
            var subDirs = Directory.GetDirectories(folderPath).OrderBy(p => Path.GetFileName(p));
            foreach (var dir in subDirs)
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("."))
                    continue;

                // デフォルトでEditorとXRは除外
                if (dirName.Equals("Editor", System.StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("XR", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BuildTreeRecursive(dir, item, depth + 1);
            }

            // ファイルを追加
            var files = Directory.GetFiles(folderPath).OrderBy(p => Path.GetFileName(p));
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".meta"))
                    continue;

                var fileItem = new FolderTreeItem(nextId, depth + 1, fileName, file, false);
                itemDict[nextId] = fileItem;
                item.AddChild(fileItem);
                nextId++;
            }
        }
        catch
        {
            // アクセス権がない等のエラーは無視
        }
    }

    /// <summary>
    /// チェックボックスの見た目上の状態（未チェック／チェック済み／一部だけチェック＝中間状態）
    /// </summary>
    private enum CheckState
    {
        Unchecked,
        Checked,
        Mixed
    }

    /// <summary>
    /// アイテム自身とその子孫全体を見て、表示すべきチェック状態を算出する。
    /// フォルダの子孫の一部だけがチェックされている場合はMixed（中間状態）を返す。
    /// これは表示専用のロジックであり、item.isChecked 自体は変更しない
    /// （選択されたパスの実体は、明示的にチェックされた項目のみ）。
    /// </summary>
    private CheckState GetCheckState(FolderTreeItem item)
    {
        if (!item.hasChildren || item.children == null || item.children.Count == 0)
        {
            return item.isChecked ? CheckState.Checked : CheckState.Unchecked;
        }

        bool anyChecked = item.isChecked;
        bool anyUnchecked = !item.isChecked;

        foreach (TreeViewItem child in item.children)
        {
            if (child is FolderTreeItem folderChild)
            {
                var childState = GetCheckState(folderChild);
                if (childState == CheckState.Mixed)
                {
                    anyChecked = true;
                    anyUnchecked = true;
                }
                else if (childState == CheckState.Checked)
                {
                    anyChecked = true;
                }
                else
                {
                    anyUnchecked = true;
                }
            }
        }

        if (anyChecked && anyUnchecked)
            return CheckState.Mixed;

        return anyChecked ? CheckState.Checked : CheckState.Unchecked;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var item = args.item as FolderTreeItem;
        if (item == null)
        {
            base.RowGUI(args);
            return;
        }

        float indent = GetContentIndent(item);
        Rect rowRect = args.rowRect;
        Rect toggleRect = new Rect(rowRect.x + indent + 4, rowRect.y + 2, 16, rowRect.height - 4);

        CheckState state = GetCheckState(item);
        bool displayValue = state == CheckState.Checked;

        bool previousMixedValue = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = (state == CheckState.Mixed);
        bool newValue = EditorGUI.Toggle(toggleRect, displayValue);
        EditorGUI.showMixedValue = previousMixedValue;

        if (newValue != displayValue)
        {
            // 中間状態からクリックした場合は「全選択」、チェック済みからは「全解除」になる
            SetCheckedRecursively(item, newValue);
            Repaint();
        }

        rowRect.x += indent + 24;
        rowRect.width -= indent + 24;

        var newArgs = args;
        newArgs.rowRect = rowRect;
        base.RowGUI(newArgs);
    }

    private void SetCheckedRecursively(FolderTreeItem item, bool value)
    {
        item.isChecked = value;
        if (item.hasChildren && item.children != null)
        {
            foreach (TreeViewItem child in item.children)
            {
                if (child is FolderTreeItem folderChild)
                {
                    SetCheckedRecursively(folderChild, value);
                }
            }
        }
    }

    public List<string> GetSelectedPaths()
    {
        var result = new List<string>();
        foreach (var item in itemDict.Values)
        {
            if (item.isChecked)
                result.Add(item.fullPath);
        }
        return result;
    }

    public void SetAllChecked(bool checked_)
    {
        foreach (var item in itemDict.Values)
        {
            item.isChecked = checked_;
        }
        Repaint();
    }
}

/// <summary>
/// フォルダ選択ウィンドウ
/// </summary>
public class FolderSelectionWindow : EditorWindow
{
    private TreeViewState treeViewState;
    private FolderSelectionTreeView treeView;
    private Vector2 scrollPosition;
    private System.Action<List<string>> onFoldersSelected;

    public static void ShowWindow(System.Action<List<string>> callback)
    {
        var window = GetWindow<FolderSelectionWindow>("Select Folders to Save");
        window.minSize = new Vector2(300, 400);
        window.onFoldersSelected = callback;
    }

    private void OnEnable()
    {
        if (treeViewState == null)
            treeViewState = new TreeViewState();

        treeView = new FolderSelectionTreeView(treeViewState);
        treeView.ExpandAll();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Select folders and files to save", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // ボタン
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", GUILayout.Width(60)))
        {
            treeView.SetAllChecked(true);
        }
        if (GUILayout.Button("None", GUILayout.Width(60)))
        {
            treeView.SetAllChecked(false);
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // ツリービュー
        Rect treeRect = GUILayoutUtility.GetRect(0, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        treeView.OnGUI(treeRect);

        EditorGUILayout.Space();

        // 決定ボタン
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Selected", GUILayout.Height(30)))
        {
            var selectedPaths = treeView.GetSelectedPaths();
            if (selectedPaths.Count > 0)
            {
                onFoldersSelected?.Invoke(selectedPaths);
                Close();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select at least one folder or file.", "OK");
            }
        }
        if (GUILayout.Button("Cancel", GUILayout.Height(30)))
        {
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

public class PackageSaveTool : EditorWindow
{
    private const string AuthorEditorPrefKey = "PackageSaveTool_AuthorName";

    // 選択した相対パス（Assets基準）を記録するマニフェストのファイル名
    private const string ManifestFileName = "_selection_manifest.json";

    /// <summary>
    /// Save時に選択された、Assetsからの相対パスの一覧を保持するマニフェスト
    /// </summary>
    [System.Serializable]
    private class SelectionManifest
    {
        public string[] relativePaths;
    }

    private Vector2 scrollPosition;
    private VersionInfo currentVersion = new VersionInfo(1, 0, 0);
    private string authorName = "Unknown";

    [MenuItem("Tools/Package Save Tool")]
    public static void ShowWindow()
    {
        GetWindow<PackageSaveTool>("Package Save Tool");
    }

    private void OnGUI()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Folder Management", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // バージョン表示・編集
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

        // 手動バージョン増加
        if (GUILayout.Button("Increment Version", GUILayout.Height(35)))
        {
            ShowVersionIncrementDialog();
        }

        EditorGUILayout.Space();
        EditorGUILayout.Separator();
        EditorGUILayout.Space();

        if (GUILayout.Button("Save Folder (With Version Control)", GUILayout.Height(40)))
        {
            FolderSelectionWindow.ShowWindow(OnFoldersSelected);
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Load Folder (Import & Fix References)", GUILayout.Height(40)))
        {
            LoadFolderWithReferenceFixing();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Copy Components from Prefab to FBX", GUILayout.Height(40)))
        {
            CopyComponentsFromPrefabToFBX();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Save: Saves selected folders/files while preserving their Assets-relative hierarchy, and records a manifest of what was selected.\n\nLoad: If a manifest is present, updates only the folders/files listed in it (relative to Assets), leaving everything else untouched. Falls back to legacy full-folder import for packages without a manifest.\n\nCopy Components: Copies components from a selected prefab to a selected FBX model.", MessageType.Info);

        GUILayout.EndScrollView();
    }

    /// <summary>
    /// バージョン増加ダイアログを表示
    /// </summary>
    private void ShowVersionIncrementDialog()
    {
        var buttons = new[] { "破壊的変更（メジャー版UP v1.0.0→v2.0.0）", "互換性あり（マイナー版UP v1.0.0→v1.1.0）", "キャンセル" };
        
        int result = EditorUtility.DisplayDialogComplex(
            "バージョン増加方法を選択",
            "このバージョンは破壊的な変更を含みますか？\n\n" +
            "【破壊的変更】\n" +
            "既存のデータ/設定と互換性がない場合（メジャーバージョン UP）\n\n" +
            "【互換性あり】\n" +
            "機能追加だが既存との互換性がある場合（マイナーバージョン UP）",
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
        else
        {
            Debug.Log("Version increment cancelled.");
        }

        SaveVersionInfo();
    }

    /// <summary>
    /// バージョン情報をJSONで保存
    /// </summary>
    private void SaveVersionInfo()
    {
        string versionFile = Path.Combine(Application.persistentDataPath, "version_info.json");
        string jsonData = JsonUtility.ToJson(currentVersion, true);
        File.WriteAllText(versionFile, jsonData);
    }

    /// <summary>
    /// バージョン情報をJSONから読み込み
    /// </summary>
    private void LoadVersionInfo()
    {
        string versionFile = Path.Combine(Application.persistentDataPath, "version_info.json");
        if (File.Exists(versionFile))
        {
            string jsonData = File.ReadAllText(versionFile);
            currentVersion = JsonUtility.FromJson<VersionInfo>(jsonData) ?? new VersionInfo(1, 0, 0);
        }
    }

    private void OnEnable()
    {
        LoadVersionInfo();
        authorName = EditorPrefs.GetString(AuthorEditorPrefKey, "Unknown");
        if (string.IsNullOrWhiteSpace(authorName))
            authorName = "Unknown";
    }

    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new System.Text.StringBuilder();
        foreach (var c in fileName)
        {
            if (!invalidChars.Contains(c))
                sanitized.Append(c);
        }
        return sanitized.ToString().Trim();
    }

    /// <summary>
    /// フルパス（"Assets/Foo/Bar/Baz" 等、ツリービューが返す "Assets" 起点の相対パス）から、
    /// "Assets/" を除いた相対パス（"Foo/Bar/Baz"）を取得する。
    /// これを保存先直下に適用することで、Assets内の階層構造を保ったままコピーできる。
    /// </summary>
    private string GetPathRelativeToAssets(string fullPath)
    {
        string normalized = fullPath.Replace('\\', '/').TrimEnd('/');

        // FilterTopLevelSelections が Path.GetFullPath() で絶対パス化することがあるため、
        // まず Application.dataPath（Assetsフォルダの絶対パス）を基準に判定する。
        string assetsAbsolute = Application.dataPath.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith(assetsAbsolute + "/", System.StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(assetsAbsolute.Length + 1);
        }

        if (normalized.Equals(assetsAbsolute, System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // ツリービューから渡される "Assets/..." 形式の相対パスにも対応
        const string prefix = "Assets/";
        if (normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(prefix.Length);
        }

        if (normalized.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // 想定外の形式の場合はファイル名のみにフォールバック（従来動作）
        return Path.GetFileName(normalized);
    }

    /// <summary>
    /// フォルダを保存し、差分があればバージョン番号を付加する
    /// </summary>
    /// <summary>
    /// フォルダ選択ウィンドウからフォルダが選択されたときのコールバック
    /// </summary>
    private void OnFoldersSelected(List<string> selectedPaths)
    {
        string destinationPath = EditorUtility.OpenFolderPanel("Select Save Destination", "", "");
        
        if (string.IsNullOrEmpty(destinationPath))
        {
            Debug.Log("Destination selection cancelled.");
            return;
        }

        // 親フォルダが選択されている場合は、その子パスを除外する
        selectedPaths = FilterTopLevelSelections(selectedPaths);

        string sanitizedAuthor = SanitizeFileName(authorName);
        if (string.IsNullOrEmpty(sanitizedAuthor))
            sanitizedAuthor = "Unknown";

        // 保存フォルダ名（名前_version のみ）
        string saveFolder = $"{sanitizedAuthor}_{currentVersion}";
        string savePath = Path.Combine(destinationPath, saveFolder);
        Directory.CreateDirectory(savePath);

        // 選択されたパスを、Assetsからの相対パスを維持したままコピーする。
        // これにより、深い階層のフォルダ/ファイルだけを選択しても、
        // 親フォルダの中身を巻き込まずに、正しい階層構造で保存できる。
        var manifestRelativePaths = new List<string>();

        foreach (var selectedPath in selectedPaths)
        {
            string relativePath = GetPathRelativeToAssets(selectedPath);
            if (string.IsNullOrEmpty(relativePath))
                continue;

            string destPath = Path.Combine(savePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(selectedPath))
            {
                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(selectedPath, destPath, true);
                manifestRelativePaths.Add(relativePath);
            }
            else if (Directory.Exists(selectedPath))
            {
                string destParentDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destParentDir))
                    Directory.CreateDirectory(destParentDir);

                CopyFolder(selectedPath, destPath);
                manifestRelativePaths.Add(relativePath);
            }
        }

        // 選択内容（Assets相対パス）をマニフェストとして保存。
        // Load時にこのマニフェストを見て、対象パスだけを更新できるようにする。
        var manifest = new SelectionManifest { relativePaths = manifestRelativePaths.ToArray() };
        string manifestPath = Path.Combine(savePath, ManifestFileName);
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));

        Debug.Log($"Folders saved successfully to: {savePath}");
        Debug.Log($"Current Version: {currentVersion}");
        
        // 保存完了後、パッチバージョンを自動増加
        currentVersion.IncrementPatch();
        SaveVersionInfo();
        Debug.Log($"Next Version: {currentVersion}");
        
        EditorUtility.RevealInFinder(savePath);
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

    private string GetImportSourceFolder(string folderPath)
    {
        string folderName = Path.GetFileName(folderPath);
        if (IsVersionedWrapperFolder(folderName))
        {
            var childDirs = Directory.GetDirectories(folderPath);
            if (childDirs.Length > 0)
            {
                Debug.Log($"Skipping wrapper folder {folderName} and importing first child {Path.GetFileName(childDirs[0])}.");
                return childDirs[0];
            }
        }

        return folderPath;
    }

    private bool IsVersionedWrapperFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return false;

        return Regex.IsMatch(folderName, @"^.+(?:[_=])[vV]?\d+\.\d+\.\d+$");
    }

    /// <summary>
    /// フォルダ名からバージョンを抽出（例：author_v1.0.0 → v1.0.0）
    /// </summary>
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

    /// <summary>
    /// 読み込み後のバージョンを更新（新しい場合は自動更新、古い場合は確認）
    /// </summary>
    private void UpdateVersionAfterLoad(VersionInfo loadedVersion)
    {
        int comparison = CompareVersions(loadedVersion, currentVersion);

        if (comparison > 0)
        {
            // 読み込んだバージョンが新しい場合
            currentVersion = loadedVersion;
            SaveVersionInfo();
            Debug.Log($"Version updated to {currentVersion} (loaded version is newer)");
        }
        else if (comparison < 0)
        {
            // 読み込んだバージョンが古い場合
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
            else
            {
                Debug.Log("Version update cancelled by user.");
            }
        }
        else
        {
            // バージョンが同じ場合
            Debug.Log($"Version is same: {currentVersion}");
        }
    }

    /// <summary>
    /// バージョンを比較（返り値: positive=v1が新しい, 0=同じ, negative=v1が古い）
    /// </summary>
    private int CompareVersions(VersionInfo v1, VersionInfo v2)
    {
        if (v1.major != v2.major)
            return v1.major.CompareTo(v2.major);
        if (v1.minor != v2.minor)
            return v1.minor.CompareTo(v2.minor);
        return v1.patch.CompareTo(v2.patch);
    }

    private void SaveFolderWithVersioning()
    {
    }

    /// <summary>
    /// フォルダをロードして、参照を修復する。
    /// 保存時に書き出したマニフェストが存在する場合は、そこに記録された
    /// Assets相対パスだけをピンポイントで更新する（他のフォルダには触れない）。
    /// マニフェストが無い（旧形式で保存された）パッケージは、従来通り
    /// フォルダ全体をユーザー指定の場所へインポートする。
    /// </summary>
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

        // --- 以下、マニフェストが無い旧形式パッケージ向けの従来ロジック ---

        string assetsFolderPath = Path.Combine(EditorApplication.applicationPath, "..", "Assets").Replace("\\", "/");
        string projectFolder = Directory.GetParent(Application.dataPath).FullName;
        string importPath = EditorUtility.SaveFolderPanel("Select Import Destination (in Assets folder)", "Assets", "");
        
        if (string.IsNullOrEmpty(importPath))
        {
            Debug.Log("Import destination cancelled.");
            return;
        }

        // ルートが author_v1.0.0 / author=v1.0.0 形式なら、最初の子フォルダを読み込む
        string sourceFolder = GetImportSourceFolder(folderPath);

        // フォルダをコピー
        string finalImportPath = Path.Combine(importPath, new DirectoryInfo(sourceFolder).Name);
        
        if (Directory.Exists(finalImportPath))
        {
            var diffInfo = GetFolderDifferences(sourceFolder, finalImportPath);
            var summary = new StringBuilder();
            summary.AppendLine($"既存フォルダが見つかりました: {finalImportPath}");
            summary.AppendLine($"追加: {diffInfo.Added.Count} 件");
            summary.AppendLine($"変更: {diffInfo.Modified.Count} 件");
            summary.AppendLine($"削除: {diffInfo.Removed.Count} 件");
            summary.AppendLine();
            summary.AppendLine("この操作で現在のデータは上書きされます。バックアップは大丈夫ですか？");

            Debug.Log("[PackageSaveTool] Folder diff details:\n" + string.Join("\n", diffInfo.GetAllLines()));

            if (!EditorUtility.DisplayDialog("Overwrite Confirmation", summary.ToString(), "上書きする", "キャンセル"))
            {
                Debug.Log("Import cancelled by user.");
                return;
            }
        }

        CopyFolder(sourceFolder, finalImportPath);

        // ロードされたフォルダのバージョンを検出・更新
        VersionInfo loadedVersion = ExtractVersionFromFolderName(folderPath);
        if (loadedVersion != null)
        {
            UpdateVersionAfterLoad(loadedVersion);
        }

        // Assetsフォルダ内のパスに変換
        string relativeImportPath = finalImportPath.Replace(projectFolder + "\\", "").Replace("\\", "/");
        
        // アセットデータベースをリフレッシュ
        AssetDatabase.Refresh();

        // 参照を修復
        FixComponentReferences(relativeImportPath);

        Debug.Log($"Folder imported and references fixed at: {relativeImportPath}");
        Debug.Log($"Loaded with Version: {currentVersion}");
        EditorUtility.RevealInFinder(finalImportPath);
    }

    /// <summary>
    /// マニフェストに記録された相対パスだけを、Assets内の対応する場所にピンポイントで更新する。
    /// 各パスごとに差分を確認し、必要であれば確認ダイアログを出す。
    /// 対象外のフォルダ/ファイルには一切触れない。
    /// </summary>
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

        string projectAssetsPath = Application.dataPath; // ".../Assets" の絶対パス
        string projectFolder = Directory.GetParent(projectAssetsPath).FullName;

        var updatedRelativePaths = new List<string>();

        foreach (var relPath in manifest.relativePaths)
        {
            if (string.IsNullOrEmpty(relPath))
                continue;

            string normalizedRel = relPath.Replace('/', Path.DirectorySeparatorChar);
            string sourcePath = Path.Combine(sourceRoot, normalizedRel);
            string destPath = Path.Combine(projectAssetsPath, normalizedRel);

            if (File.Exists(sourcePath))
            {
                bool existed = File.Exists(destPath);
                if (existed && !FileHashEquals(sourcePath, destPath))
                {
                    bool proceed = EditorUtility.DisplayDialog(
                        "Overwrite Confirmation",
                        $"更新対象: Assets/{relPath.Replace('\\', '/')}\n" +
                        "既存のファイルと内容が異なります。上書きしますか？",
                        "上書きする", "スキップ");

                    if (!proceed)
                        continue;
                }

                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, destPath, true);
                updatedRelativePaths.Add(relPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                bool existed = Directory.Exists(destPath);
                if (existed)
                {
                    var diffInfo = GetFolderDifferences(sourcePath, destPath);
                    if (diffInfo.Added.Count > 0 || diffInfo.Modified.Count > 0 || diffInfo.Removed.Count > 0)
                    {
                        var summary = new StringBuilder();
                        summary.AppendLine($"更新対象: Assets/{relPath.Replace('\\', '/')}");
                        summary.AppendLine($"追加: {diffInfo.Added.Count} 件");
                        summary.AppendLine($"変更: {diffInfo.Modified.Count} 件");
                        summary.AppendLine($"削除: {diffInfo.Removed.Count} 件");
                        summary.AppendLine();
                        summary.AppendLine("このフォルダだけが更新されます（他のフォルダは変更されません）。よろしいですか？");

                        Debug.Log($"[PackageSaveTool] Diff for Assets/{relPath.Replace('\\', '/')}:\n" + string.Join("\n", diffInfo.GetAllLines()));

                        if (!EditorUtility.DisplayDialog("Overwrite Confirmation", summary.ToString(), "上書きする", "スキップ"))
                        {
                            continue;
                        }
                    }
                }

                CopyFolder(sourcePath, destPath);
                updatedRelativePaths.Add(relPath);
            }
            else
            {
                Debug.LogWarning($"Source path listed in manifest was not found in package: {relPath}");
            }
        }

        // 保存フォルダ名（例: Author_v1.0.0）からバージョンを検出・更新
        VersionInfo loadedVersion = ExtractVersionFromFolderName(sourceRoot);
        if (loadedVersion != null)
        {
            UpdateVersionAfterLoad(loadedVersion);
        }

        if (updatedRelativePaths.Count == 0)
        {
            Debug.Log("No folders/files were updated.");
            return;
        }

        // アセットデータベースをリフレッシュ
        AssetDatabase.Refresh();

        // 更新した各パスについて参照を修復
        foreach (var relPath in updatedRelativePaths)
        {
            string relativeAssetPath = ("Assets/" + relPath).Replace('\\', '/');
            FixComponentReferences(relativeAssetPath);
        }

        Debug.Log($"Updated {updatedRelativePaths.Count} folder(s)/file(s) using manifest: " +
            string.Join(", ", updatedRelativePaths.Select(p => "Assets/" + p.Replace('\\', '/'))));
        Debug.Log($"Loaded with Version: {currentVersion}");

        string firstUpdatedDest = Path.Combine(projectAssetsPath, updatedRelativePaths[0].Replace('/', Path.DirectorySeparatorChar));
        EditorUtility.RevealInFinder(firstUpdatedDest);
    }

    private class FolderDiffInfo
    {
        public List<string> Added = new List<string>();
        public List<string> Modified = new List<string>();
        public List<string> Removed = new List<string>();

        public IEnumerable<string> GetAllLines()
        {
            foreach (var line in Added)
                yield return $"追加: {line}";
            foreach (var line in Modified)
                yield return $"変更: {line}";
            foreach (var line in Removed)
                yield return $"削除: {line}";
        }
    }

    /// <summary>
    /// 2つのフォルダの差分をチェック
    /// </summary>
    private bool FolderHasChanges(string sourceFolder, string destinationFolder)
    {
        if (!Directory.Exists(destinationFolder))
            return true;

        var sourceFiles = GetAllFiles(sourceFolder);
        var destFiles = GetAllFiles(destinationFolder);

        // ファイル数が異なる場合は差分あり
        if (sourceFiles.Count != destFiles.Count)
            return true;

        // ファイルのハッシュを比較
        foreach (var file in sourceFiles)
        {
            string relativePath = file.Substring(sourceFolder.Length + 1);
            string destFile = Path.Combine(destinationFolder, relativePath);

            if (!File.Exists(destFile))
                return true;

            if (!FileHashEquals(file, destFile))
                return true;
        }

        return false;
    }

    private FolderDiffInfo GetFolderDifferences(string sourceFolder, string destinationFolder)
    {
        var info = new FolderDiffInfo();
        var sourceFiles = new Dictionary<string, string>();
        var destFiles = new Dictionary<string, string>();

        foreach (var filePath in GetAllFiles(sourceFolder))
        {
            string relPath = RelativePath(filePath, sourceFolder);
            sourceFiles[relPath] = filePath;
        }

        foreach (var filePath in GetAllFiles(destinationFolder))
        {
            string relPath = RelativePath(filePath, destinationFolder);
            destFiles[relPath] = filePath;
        }

        foreach (var kv in sourceFiles)
        {
            if (!destFiles.ContainsKey(kv.Key))
            {
                info.Added.Add(kv.Key);
                continue;
            }

            if (!FileHashEquals(kv.Value, destFiles[kv.Key]))
                info.Modified.Add(kv.Key);
        }

        foreach (var kv in destFiles)
        {
            if (!sourceFiles.ContainsKey(kv.Key))
                info.Removed.Add(kv.Key);
        }

        return info;
    }

    private string RelativePath(string filePath, string rootFolder)
    {
        return filePath.Substring(rootFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// ファイルのハッシュ値を比較
    /// </summary>
    private bool FileHashEquals(string file1, string file2)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash1 = md5.ComputeHash(File.ReadAllBytes(file1));
            byte[] hash2 = md5.ComputeHash(File.ReadAllBytes(file2));
            return hash1.SequenceEqual(hash2);
        }
    }

    /// <summary>
    /// フォルダ内のすべてのファイルを取得
    /// </summary>
    private List<string> GetAllFiles(string folderPath)
    {
        return Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta"))
            .ToList();
    }

    /// <summary>
    /// フォルダをコピー
    /// </summary>
    private void CopyFolder(string sourcePath, string destPath)
    {
        if (Directory.Exists(destPath))
            Directory.Delete(destPath, true);

        Directory.CreateDirectory(destPath);

        foreach (var file in Directory.GetFiles(sourcePath))
        {
            string fileName = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destPath, fileName), true);
        }

        foreach (var folder in Directory.GetDirectories(sourcePath))
        {
            string folderName = Path.GetFileName(folder);
            CopyFolder(folder, Path.Combine(destPath, folderName));
        }
    }

    /// <summary>
    /// コンポーネントの参照を修復（VRC、Animation関連）
    /// </summary>
    private void FixComponentReferences(string folderPath)
    {
        var gameObjects = GetAllGameObjectsInFolder(folderPath);

        foreach (var obj in gameObjects)
        {
            FixAnimatorReferences(obj);
            FixVRCComponentReferences(obj);
            FixCustomScriptReferences(obj);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.SetDirty(Selection.activeObject);
    }

    /// <summary>
    /// フォルダ内のすべてのGameObjectを取得
    /// </summary>
    private List<GameObject> GetAllGameObjectsInFolder(string folderPath)
    {
        var result = new List<GameObject>();
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });

        foreach (var guid in prefabs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                result.Add(prefab);
        }

        return result;
    }

    /// <summary>
    /// Animator参照を修復
    /// </summary>
    private void FixAnimatorReferences(GameObject prefab)
    {
        var animators = prefab.GetComponentsInChildren<Animator>(true);
        foreach (var animator in animators)
        {
            if (animator.avatar == null)
            {
                // アバター参照を探す
                var avatar = prefab.GetComponentInChildren<Animator>(true)?.avatar 
                    ?? Resources.Load<Avatar>("Avatars/Default");
                if (avatar != null)
                    animator.avatar = avatar;
            }

            // AnimatorControllerの参照を確認
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"Animator on {animator.gameObject.name} has no controller", animator.gameObject);
            }
        }
    }

    /// <summary>
    /// VRCコンポーネント参照を修復
    /// </summary>
    private void FixVRCComponentReferences(GameObject prefab)
    {
        var allComponents = prefab.GetComponentsInChildren<Component>(true);

        foreach (var component in allComponents)
        {
            if (component == null) continue;

            string componentTypeName = component.GetType().Name;

            // VRCPhysBone、VRCAvatarDescriptor等のVRCコンポーネント
            if (componentTypeName.StartsWith("VRC"))
            {
                FixTransformReferencesInComponent(component, prefab);
            }
        }
    }

    /// <summary>
    /// カスタムスクリプトの参照を修復
    /// </summary>
    private void FixCustomScriptReferences(GameObject prefab)
    {
        var allComponents = prefab.GetComponentsInChildren<Component>(true);

        foreach (var component in allComponents)
        {
            if (component == null) continue;

            // MonoBehaviourのシリアライズフィールドを修復
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.GetIterator();

            while (property.NextVisible(true))
            {
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    // 参照がnoneの場合、同じフォルダ内で同名のアセットを探す
                    if (property.objectReferenceValue == null && !string.IsNullOrEmpty(property.name))
                    {
                        var foundAsset = FindAssetByNameInFolder(property.name, Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)));
                        if (foundAsset != null)
                        {
                            property.objectReferenceValue = foundAsset;
                            serializedObject.ApplyModifiedProperties();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// コンポーネント内のTransform参照を修復
    /// </summary>
    private void FixTransformReferencesInComponent(Component component, GameObject rootPrefab)
    {
        var serializedObject = new SerializedObject(component);
        var property = serializedObject.GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                // Transform型の参照
                if (property.objectReferenceValue == null && property.name.Contains("Transform"))
                {
                    // 同じ名前のTransformをrootPrefab内で探す
                    var foundTransform = rootPrefab.transform.Find(property.name);
                    if (foundTransform != null)
                    {
                        property.objectReferenceValue = foundTransform;
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            }
        }
    }

    /// <summary>
    /// フォルダ内でアセット名から該当するアセットを探す
    /// </summary>
    private Object FindAssetByNameInFolder(string assetName, string folderPath)
    {
        var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(assetName), new[] { folderPath });
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    /// <summary>
    /// プレハブのコンポーネントをFBXにコピー
    /// </summary>
    private void CopyComponentsFromPrefabToFBX()
    {
        // ソースプレハブを選択
        string sourcePrefabPath = EditorUtility.OpenFilePanel("Select Source Prefab", "Assets", "prefab");
        if (string.IsNullOrEmpty(sourcePrefabPath))
        {
            Debug.Log("Source prefab selection cancelled.");
            return;
        }

        // Assetsフォルダ内の相対パスに変換
        string relativeSourcePath = GetRelativeAssetPath(sourcePrefabPath);
        if (string.IsNullOrEmpty(relativeSourcePath))
        {
            EditorUtility.DisplayDialog("Error", "Selected prefab is not in the Assets folder.", "OK");
            return;
        }

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(relativeSourcePath);
        if (sourcePrefab == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to load source prefab.", "OK");
            return;
        }

        // ターゲットFBXを選択
        string targetFBXPath = EditorUtility.OpenFilePanel("Select Target FBX", "Assets", "fbx");
        if (string.IsNullOrEmpty(targetFBXPath))
        {
            Debug.Log("Target FBX selection cancelled.");
            return;
        }

        // Assetsフォルダ内の相対パスに変換
        string relativeTargetPath = GetRelativeAssetPath(targetFBXPath);
        if (string.IsNullOrEmpty(relativeTargetPath))
        {
            EditorUtility.DisplayDialog("Error", "Selected FBX is not in the Assets folder.", "OK");
            return;
        }

        GameObject targetFBX = AssetDatabase.LoadAssetAtPath<GameObject>(relativeTargetPath);
        if (targetFBX == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to load target FBX.", "OK");
            return;
        }

        // FBXをプレハブとして保存するパスを選択
        string savePath = EditorUtility.SaveFilePanel("Save FBX as Prefab", "Assets", Path.GetFileNameWithoutExtension(relativeTargetPath) + "_withComponents", "prefab");
        if (string.IsNullOrEmpty(savePath))
        {
            Debug.Log("Save path selection cancelled.");
            return;
        }

        string relativeSavePath = GetRelativeAssetPath(savePath);
        if (string.IsNullOrEmpty(relativeSavePath))
        {
            EditorUtility.DisplayDialog("Error", "Save path must be in the Assets folder.", "OK");
            return;
        }

        // シーンにFBXをインスタンス化
        GameObject instance = PrefabUtility.InstantiatePrefab(targetFBX) as GameObject;
        if (instance == null)
        {
            // プレハブでない場合、直接インスタンス化
            instance = Object.Instantiate(targetFBX);
        }

        // ソースプレハブのコンポーネントをコピー
        CopyComponents(sourcePrefab, instance, sourcePrefab, instance);

        // 新しいプレハブとして保存
        PrefabUtility.SaveAsPrefabAsset(instance, relativeSavePath);

        // シーンから削除
        Object.DestroyImmediate(instance);

        // アセットデータベースをリフレッシュ
        AssetDatabase.Refresh();

        Debug.Log($"Components copied from {relativeSourcePath} to {relativeSavePath}");
        EditorUtility.RevealInFinder(savePath);
    }

    /// <summary>
    /// 絶対パスをAssetsフォルダ内の相対パスに変換
    /// </summary>
    private string GetRelativeAssetPath(string absolutePath)
    {
        string dataPath = Application.dataPath;
        if (absolutePath.StartsWith(dataPath))
        {
            return "Assets" + absolutePath.Substring(dataPath.Length);
        }
        return null;
    }

    /// <summary>
    /// ソースGameObjectのコンポーネントをターゲットGameObjectにコピー
    /// </summary>
    private void CopyComponents(GameObject source, GameObject target, GameObject sourceRoot, GameObject targetRoot)
    {
        Component[] sourceComponents = source.GetComponents<Component>();

        foreach (Component sourceComponent in sourceComponents)
        {
            if (sourceComponent is Transform)
                continue; // Transformはコピーしない

            System.Type componentType = sourceComponent.GetType();

            // 同じタイプのコンポーネントが既に存在するかチェック
            Component existingComponent = target.GetComponent(componentType);
            if (existingComponent != null)
            {
                // 既存のコンポーネントのプロパティをソースからコピー（FBX関連参照はスキップ）
                CopyComponentProperties(sourceComponent, existingComponent, sourceRoot, targetRoot);
            }
            else
            {
                // 新しいコンポーネントを追加
                Component newComponent = target.AddComponent(componentType);

                // シリアライズされたプロパティをコピー（FBX関連参照はスキップ）
                CopyComponentProperties(sourceComponent, newComponent, sourceRoot, targetRoot);
            }
        }

        // 子オブジェクトも再帰的にコピー
        for (int i = 0; i < source.transform.childCount; i++)
        {
            Transform sourceChild = source.transform.GetChild(i);
            Transform targetChild = target.transform.Find(sourceChild.name);

            if (targetChild == null)
            {
                // 新しい子オブジェクトを作成
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

    /// <summary>
    /// コンポーネントのプロパティをコピー（FBX関連参照はスキップ）
    /// </summary>
    private void CopyComponentProperties(Component sourceComponent, Component targetComponent, GameObject sourceRoot, GameObject targetRoot)
    {
        SerializedObject sourceSO = new SerializedObject(sourceComponent);
        SerializedObject targetSO = new SerializedObject(targetComponent);

        SerializedProperty sourceProp = sourceSO.GetIterator();
        while (sourceProp.NextVisible(true))
        {
            // FBXに関わる参照をスキップ（materials, bones, meshesなど）
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
}
