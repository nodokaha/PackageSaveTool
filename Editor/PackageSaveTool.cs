using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
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

            var subDirs = Directory.GetDirectories(folderPath).OrderBy(p => Path.GetFileName(p));
            foreach (var dir in subDirs)
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("."))
                    continue;

                if (dirName.Equals("Editor", System.StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("XR", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BuildTreeRecursive(dir, item, depth + 1);
            }

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
            // アクセス権エラー等はスキップ
        }
    }

    private enum CheckState
    {
        Unchecked,
        Checked,
        Mixed
    }

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

        Rect treeRect = GUILayoutUtility.GetRect(0, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        treeView.OnGUI(treeRect);

        EditorGUILayout.Space();

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

/// <summary>
/// 修復・検出ログのデータ構造
/// </summary>
public class AnimatorFixReport
{
    public string ControllerName;
    public string ControllerPath;
    public string StateName;
    public string ReassignedClipName;
    public string ClipPath;
    public bool IsFixed; // true: 修復済み, false: Missing検出のみ
}

/// <summary>
/// Missing検出・修復結果リスト表示用ウィンドウ
/// </summary>
public class AnimatorFixResultWindow : EditorWindow
{
    private List<AnimatorFixReport> reports = new List<AnimatorFixReport>();
    private Vector2 scrollPos;
    private string windowTitleText = "Animator Controller Analysis Results";

    public static void ShowReport(List<AnimatorFixReport> reportList, string title = "Animator Results")
    {
        var window = GetWindow<AnimatorFixResultWindow>("Animator Results");
        window.minSize = new Vector2(650, 400);
        window.reports = reportList ?? new List<AnimatorFixReport>();
        window.windowTitleText = title;
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"{windowTitleText} (全 {reports.Count} 件)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (reports.Count == 0)
        {
            EditorGUILayout.HelpBox("該当する AnimationClip の項目は見つかりませんでした。", MessageType.Info);
            return;
        }

        // テーブルヘッダー
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(70));
        GUILayout.Label("Animator Controller", EditorStyles.boldLabel, GUILayout.Width(180));
        GUILayout.Label("State / Target", EditorStyles.boldLabel, GUILayout.Width(150));
        GUILayout.Label("Clip Info", EditorStyles.boldLabel, GUILayout.Width(180));
        GUILayout.Label("操作", EditorStyles.boldLabel, GUILayout.Width(60));
        EditorGUILayout.EndHorizontal();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        foreach (var r in reports)
        {
            EditorGUILayout.BeginHorizontal(GUI.skin.box);

            // ステータス表示
            if (r.IsFixed)
            {
                GUI.color = Color.green;
                GUILayout.Label("[Fixed]", GUILayout.Width(70));
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.red;
                GUILayout.Label("[Missing]", GUILayout.Width(70));
                GUI.color = Color.white;
            }

            // コントローラー名・ステート名・クリップ名
            GUILayout.Label(r.ControllerName, GUILayout.Width(180));
            GUILayout.Label(r.StateName, GUILayout.Width(150));
            GUILayout.Label(string.IsNullOrEmpty(r.ReassignedClipName) ? "None (Missing)" : r.ReassignedClipName, GUILayout.Width(180));

            // Select ボタン（該当アセットにフォーカス）
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(r.ControllerPath);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("閉じる", GUILayout.Height(30)))
        {
            Close();
        }
    }
}

/// <summary>
/// AnimatorController 内のアニメーション欠落（Missing）の自動検知および修復クラス
/// </summary>
public static class AnimatorControllerFixer
{
    /// <summary>
    /// Missingの項目のみをスキップ（検出のみ）
    /// </summary>
    public static List<AnimatorFixReport> ScanMissingClips(AnimatorController controller)
    {
        var reports = new List<AnimatorFixReport>();
        if (controller == null) return reports;

        string controllerPath = AssetDatabase.GetAssetPath(controller);

        foreach (var layer in controller.layers)
        {
            ScanStateMachine(layer.stateMachine, controller.name, controllerPath, reports);
        }

        return reports;
    }

    private static void ScanStateMachine(AnimatorStateMachine stateMachine, string controllerName, string controllerPath, List<AnimatorFixReport> reports)
    {
        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            if (state == null) continue;

            if (state.motion is BlendTree blendTree)
            {
                ScanBlendTree(blendTree, controllerName, controllerPath, reports);
            }
            else if (IsMotionMissing(state, out _))
            {
                reports.Add(new AnimatorFixReport
                {
                    ControllerName = controllerName,
                    ControllerPath = controllerPath,
                    StateName = state.name,
                    ReassignedClipName = "",
                    ClipPath = "",
                    IsFixed = false
                });
            }
        }

        foreach (var subMachine in stateMachine.stateMachines)
        {
            ScanStateMachine(subMachine.stateMachine, controllerName, controllerPath, reports);
        }
    }

    private static void ScanBlendTree(BlendTree blendTree, string controllerName, string controllerPath, List<AnimatorFixReport> reports)
    {
        foreach (var child in blendTree.children)
        {
            if (child.motion is BlendTree subTree)
            {
                ScanBlendTree(subTree, controllerName, controllerPath, reports);
            }
            else if (child.motion == null)
            {
                reports.Add(new AnimatorFixReport
                {
                    ControllerName = controllerName,
                    ControllerPath = controllerPath,
                    StateName = $"BlendTree ({blendTree.name})",
                    ReassignedClipName = "",
                    ClipPath = "",
                    IsFixed = false
                });
            }
        }
    }

    /// <summary>
    /// Missing項目を検知して名前から自動補完（修復処理）
    /// </summary>
    public static List<AnimatorFixReport> DetectAndFixMissingClips(AnimatorController controller)
    {
        var reports = new List<AnimatorFixReport>();
        if (controller == null) return reports;

        string controllerPath = AssetDatabase.GetAssetPath(controller);
        var allClips = FindAllAnimationClipsInProject();

        foreach (var layer in controller.layers)
        {
            FixStateMachine(layer.stateMachine, controller.name, controllerPath, allClips, reports);
        }

        if (reports.Count > 0)
        {
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimatorFixer] '{controller.name}' 内の欠落したアニメーション参照を {reports.Count} 件自動修復しました。");
        }

        return reports;
    }

    private static void FixStateMachine(
        AnimatorStateMachine stateMachine,
        string controllerName,
        string controllerPath,
        Dictionary<string, (AnimationClip clip, string path)> clipCache,
        List<AnimatorFixReport> reports)
    {
        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            if (state == null) continue;

            if (state.motion is BlendTree blendTree)
            {
                FixBlendTree(blendTree, controllerName, controllerPath, clipCache, reports);
            }
            else if (IsMotionMissing(state, out string missingClipName))
            {
                if (!string.IsNullOrEmpty(missingClipName) && clipCache.TryGetValue(missingClipName, out var found))
                {
                    state.motion = found.clip;
                    reports.Add(new AnimatorFixReport
                    {
                        ControllerName = controllerName,
                        ControllerPath = controllerPath,
                        StateName = state.name,
                        ReassignedClipName = found.clip.name,
                        ClipPath = found.path,
                        IsFixed = true
                    });
                }
            }
        }

        foreach (var subMachine in stateMachine.stateMachines)
        {
            FixStateMachine(subMachine.stateMachine, controllerName, controllerPath, clipCache, reports);
        }
    }

    private static void FixBlendTree(
        BlendTree blendTree,
        string controllerName,
        string controllerPath,
        Dictionary<string, (AnimationClip clip, string path)> clipCache,
        List<AnimatorFixReport> reports)
    {
        var children = blendTree.children;
        bool isModified = false;

        for (int i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child.motion is BlendTree subTree)
            {
                FixBlendTree(subTree, controllerName, controllerPath, clipCache, reports);
            }
            else if (child.motion == null)
            {
                string searchKey = blendTree.name;
                if (clipCache.TryGetValue(searchKey, out var found))
                {
                    child.motion = found.clip;
                    children[i] = child;
                    isModified = true;

                    reports.Add(new AnimatorFixReport
                    {
                        ControllerName = controllerName,
                        ControllerPath = controllerPath,
                        StateName = $"BlendTree ({blendTree.name})",
                        ReassignedClipName = found.clip.name,
                        ClipPath = found.path,
                        IsFixed = true
                    });
                }
            }
        }

        if (isModified)
        {
            blendTree.children = children;
        }
    }

    private static bool IsMotionMissing(AnimatorState state, out string originalName)
    {
        originalName = string.Empty;

        if (state.motion == null)
        {
            SerializedObject so = new SerializedObject(state);
            SerializedProperty motionProp = so.FindProperty("m_Motion");

            if (motionProp != null && motionProp.objectReferenceInstanceIDValue != 0 && motionProp.objectReferenceValue == null)
            {
                originalName = state.name;
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, (AnimationClip clip, string path)> FindAllAnimationClipsInProject()
    {
        var dict = new Dictionary<string, (AnimationClip, string)>(System.StringComparer.OrdinalIgnoreCase);
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null && !dict.ContainsKey(clip.name))
            {
                dict.Add(clip.name, (clip, path));
            }
        }

        return dict;
    }
}

public class PackageSaveTool : EditorWindow
{
    private const string AuthorEditorPrefKey = "PackageSaveTool_AuthorName";
    private const string IntegrationModeEditorPrefKey = "PackageSaveTool_IntegrationMode";
    private const string ManifestFileName = "_selection_manifest.json";

    [System.Serializable]
    private class SelectionManifest
    {
        public string[] relativePaths;
    }

    private Vector2 scrollPosition;
    private VersionInfo currentVersion = new VersionInfo(1, 0, 0);
    private string authorName = "Unknown";
    private bool integrationMode = false;

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
            LoadFolderWithReferenceFixing();
        }

        EditorGUILayout.Space();
        EditorGUILayout.Separator();
        EditorGUILayout.Space();

        // Missing 検出専用ボタン
        if (GUILayout.Button("1. Scan Missing Animation Clips", GUILayout.Height(30)))
        {
            ScanAllAnimatorControllers();
        }

        EditorGUILayout.Space();

        // 検出＆自動修復ボタン
        if (GUILayout.Button("2. Fix Animator Controllers Missing Clips", GUILayout.Height(30)))
        {
            FixAllAnimatorControllers();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Save: 依存関係を全自動検出して一括保存します。\n\nScan: 変更を加えずに Missing 状態の State / BlendTree をリスト表示します。\n\nFix: 自動修復を実行し、結果をリストで表示します。", MessageType.Info);

        GUILayout.EndScrollView();
    }

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

    private void OnEnable()
    {
        LoadVersionInfo();
        authorName = EditorPrefs.GetString(AuthorEditorPrefKey, "Unknown");
        if (string.IsNullOrWhiteSpace(authorName))
            authorName = "Unknown";
        integrationMode = EditorPrefs.GetBool(IntegrationModeEditorPrefKey, false);
    }

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
        {
            return normalized.Substring(assetsAbsolute.Length + 1);
        }

        if (normalized.Equals(assetsAbsolute, System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        const string prefix = "Assets/";
        if (normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(prefix.Length);
        }

        if (normalized.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return Path.GetFileName(normalized);
    }

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
        Debug.Log($"Current Version: {currentVersion}");

        currentVersion.IncrementPatch();
        SaveVersionInfo();
        Debug.Log($"Next Version: {currentVersion}");

        EditorUtility.RevealInFinder(savePath);
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
            FixAllAnimatorControllers();
            return;
        }

        Debug.LogWarning("Manifest file missing. Fallback to standard directory copy.");
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

    /// <summary>
    /// プロジェクト内の Animator Controller から Missing 項目を検出しリスト表示する（修正なし）
    /// </summary>
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

    /// <summary>
    /// プロジェクト内のすべての Animator Controller に対して自動修復を適用し、結果を別ウィンドウでリスト表示する
    /// </summary>
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
}
