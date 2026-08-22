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
                treeView.SetAllChecked(true);
            if (GUILayout.Button("None", GUILayout.Width(60)))
                treeView.SetAllChecked(false);
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
                Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
