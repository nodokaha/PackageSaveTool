using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PackageSaveTool
{
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
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.BeginHorizontal();

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
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(r.ControllerPath);
                    if (obj != null)
                    {
                        EditorGUIUtility.PingObject(obj);
                        Selection.activeObject = obj;
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(r.Note))
                    EditorGUILayout.HelpBox(r.Note, MessageType.Info);
                else if (!string.IsNullOrEmpty(r.ClipPath))
                    EditorGUILayout.LabelField(r.ClipPath, EditorStyles.miniLabel);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("閉じる", GUILayout.Height(30)))
                Close();
        }
    }
}
