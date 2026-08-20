using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PackageSaveTool
{
    #region 差分結果表示ウィンドウ
    public class DiffResultWindow : EditorWindow
    {
        private DetailedFolderDiffInfo diffInfo;
        private string sourcePath;
        private string destPath;
        private Vector2 scrollPosition;
        private Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();
        private Action onConfirmCallback;

        public static void ShowWindow(DetailedFolderDiffInfo diff, string source, string dest, Action onConfirm)
        {
            var win = GetWindow<DiffResultWindow>("Folder Diff Result");
            win.diffInfo = diff;
            win.sourcePath = source;
            win.destPath = dest;
            win.onConfirmCallback = onConfirm;
            win.minSize = new Vector2(500, 400);
            win.Show();
        }

        private void OnGUI()
        {
            if (diffInfo == null || diffInfo.FileDetails == null)
            {
                EditorGUILayout.HelpBox("差分データがありません。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            GUILayout.Label("フォルダ変更差分結果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("比較元 (New):", sourcePath, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("比較先 (Old):", destPath, EditorStyles.miniLabel);
            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var detail in diffInfo.FileDetails)
            {
                Color defaultColor = GUI.color;
                switch (detail.Status)
                {
                    case "新規追加":
                    case "追加":     GUI.color = Color.green; break;
                    case "削除":     GUI.color = new Color(1f, 0.4f, 0.4f); break;
                    case "変更あり":
                    case "パラメータ変更": GUI.color = Color.yellow; break;
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

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("読み込みを実行（上書き/統合）", GUILayout.Height(35)))
            {
                onConfirmCallback?.Invoke();
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
    #endregion
}
