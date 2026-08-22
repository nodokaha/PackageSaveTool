using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PackageSaveTool
{
    public class DiffResultWindow : EditorWindow
    {
        private DetailedFolderDiffInfo diffInfo;
        private string sourcePath;
        private string destPath;
        private Vector2 scrollPosition;
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
        private string confirmButtonLabel = "読み込みを実行（上書き/統合）";
        private ModalState modalState;

        private sealed class ModalState
        {
            public bool Confirmed;
        }

        public static bool Confirm(DetailedFolderDiffInfo diff, string source, string dest, string confirmLabel)
        {
            var state = new ModalState();
            var win = CreateInstance<DiffResultWindow>();
            win.titleContent = new GUIContent("Folder Diff Result");
            win.diffInfo = diff;
            win.sourcePath = source;
            win.destPath = dest;
            win.confirmButtonLabel = string.IsNullOrEmpty(confirmLabel) ? "実行" : confirmLabel;
            win.minSize = new Vector2(500, 400);
            win.modalState = state;
            win.ShowModal();
            return state.Confirmed;
        }

        private void OnGUI()
        {
            if (diffInfo == null || diffInfo.FileDetails == null)
            {
                EditorGUILayout.HelpBox("差分データがありません。", MessageType.Info);
                DrawButtons();
                return;
            }

            EditorGUILayout.Space();
            GUILayout.Label("フォルダ変更差分結果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("比較元 (New):", sourcePath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("比較先 (Old):", destPath, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox($"差分 {diffInfo.FileDetails.Count} 件。実行すると比較先へ反映されます。", MessageType.Info);
            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var detail in diffInfo.FileDetails)
            {
                Color defaultColor = GUI.color;
                switch (detail.Status)
                {
                    case DiffStatus.Added:
                    case "追加":
                        GUI.color = Color.green;
                        break;
                    case DiffStatus.Removed:
                        GUI.color = new Color(1f, 0.4f, 0.4f);
                        break;
                    case DiffStatus.Modified:
                    case "パラメータ変更":
                        GUI.color = Color.yellow;
                        break;
                }

                EditorGUILayout.BeginVertical("box");
                GUI.color = defaultColor;

                EditorGUILayout.LabelField($"[{detail.Status}] {detail.RelativePath}", EditorStyles.boldLabel);

                if (detail.PropertyDiffs != null && detail.PropertyDiffs.Count > 0)
                {
                    if (!foldoutStates.ContainsKey(detail.RelativePath))
                        foldoutStates[detail.RelativePath] = false;

                    foldoutStates[detail.RelativePath] = EditorGUILayout.Foldout(
                        foldoutStates[detail.RelativePath],
                        $"   └ パラメータ変更 ({detail.PropertyDiffs.Count} 件)"
                    );

                    if (foldoutStates[detail.RelativePath])
                    {
                        EditorGUI.indentLevel++;
                        foreach (var prop in detail.PropertyDiffs)
                        {
                            EditorGUILayout.LabelField(prop.PropertyName, EditorStyles.boldLabel);
                            EditorGUILayout.LabelField($"    Old: {prop.OldValue}");
                            EditorGUILayout.LabelField($"    New: {prop.NewValue}");
                            EditorGUILayout.Space(2);
                        }
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
            DrawButtons();
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(confirmButtonLabel, GUILayout.Height(35)))
            {
                if (modalState != null)
                    modalState.Confirmed = true;
                Close();
            }
            if (GUILayout.Button("キャンセル", GUILayout.Height(35)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }
    }
}
