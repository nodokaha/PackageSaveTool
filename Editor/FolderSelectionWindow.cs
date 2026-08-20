using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PackageSaveTool
{
    /// <summary>
    /// フォルダ選択ウィンドウ
    /// </summary>
    public class FolderSelectionWindow : EditorWindow
    {
        private TreeViewState treeViewState;
        private FolderSelectionTreeView treeView;
        private Action<List<string>> onFoldersSelected;
        private DetailedFolderDiffInfo currentDiffInfo;
        private Vector2 leftScrollPos;
        private Vector2 rightScrollPos;
        private FileDiffDetail selectedFile;

        private void DrawDiffView()
        {
            if (currentDiffInfo == null || currentDiffInfo.FileDetails.Count == 0)
            {
                EditorGUILayout.HelpBox("変更差分はありません。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            // 左パネル：ファイル一覧
            EditorGUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(250));
            EditorGUILayout.LabelField("変更ファイル一覧", EditorStyles.boldLabel);
            leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);

            foreach (var file in currentDiffInfo.FileDetails)
            {
                GUI.backgroundColor = (selectedFile == file) ? Color.cyan : Color.white;
                if (GUILayout.Button($"[{file.Status}] {file.RelativePath}", EditorStyles.label))
                {
                    selectedFile = file;
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // 右パネル：パラメータ変更一覧
            EditorGUILayout.BeginVertical(GUI.skin.box);
            if (selectedFile != null)
            {
                EditorGUILayout.LabelField($"パラメータ変更点: {selectedFile.RelativePath}", EditorStyles.boldLabel);
                rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);

                if (selectedFile.PropertyDiffs.Count > 0)
                {
                    // ヘッダー表示
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    EditorGUILayout.LabelField("パラメータ名 (Key)", EditorStyles.boldLabel, GUILayout.Width(200));
                    EditorGUILayout.LabelField("変更前 (Old)", EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField("変更後 (New)", EditorStyles.boldLabel, GUILayout.Width(150));
                    EditorGUILayout.EndHorizontal();

                    // リスト表示
                    foreach (var prop in selectedFile.PropertyDiffs)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(prop.PropertyName, GUILayout.Width(200));

                        GUI.contentColor = new Color(0.9f, 0.4f, 0.4f); // 赤系
                        EditorGUILayout.LabelField(prop.OldValue, GUILayout.Width(150));

                        GUI.contentColor = new Color(0.3f, 0.8f, 0.3f); // 緑系
                        EditorGUILayout.LabelField(prop.NewValue, GUILayout.Width(150));

                        GUI.contentColor = Color.white;
                        EditorGUILayout.EndHorizontal();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("検出されたパラメータ変更はありません。（構造の変更またはバイナリ形式）");
                }

                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("左のリストからファイルを選択してください。", MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        public static void ShowWindow(Action<List<string>> callback)
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

                GUILayout.Label(r.ControllerName, GUILayout.Width(180));
                GUILayout.Label(r.StateName, GUILayout.Width(150));
                GUILayout.Label(string.IsNullOrEmpty(r.ReassignedClipName) ? "None (Missing)" : r.ReassignedClipName, GUILayout.Width(180));

                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.ControllerPath);
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
}
