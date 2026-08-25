using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace PackageSaveTool
{
    /// <summary>
    /// ツリービュー用のアイテム（チェックボックス付き）
    /// </summary>
    public class FolderTreeItem : TreeViewItem
    {
        public bool isChecked;
        public bool isMixed;
        public string fullPath = "";
        public bool isFolder;

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
        private readonly Dictionary<int, FolderTreeItem> itemDict = new Dictionary<int, FolderTreeItem>();
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

            const string assetsPath = "Assets";
            if (Directory.Exists(assetsPath))
                BuildTreeRecursive(assetsPath, root, 0);

            return root;
        }

        private void BuildTreeRecursive(string folderPath, TreeViewItem parent, int depth)
        {
            try
            {
                var item = new FolderTreeItem(nextId, depth, Path.GetFileName(folderPath), folderPath, true);
                itemDict[nextId] = item;
                parent.AddChild(item);
                nextId++;

                foreach (var dir in Directory.GetDirectories(folderPath).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith("."))
                        continue;

                    BuildTreeRecursive(dir, item, depth + 1);
                }

                foreach (var file in Directory.GetFiles(folderPath).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(".") || fileName.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileItem = new FolderTreeItem(nextId, depth + 1, fileName, file, false);
                    itemDict[nextId] = fileItem;
                    item.AddChild(fileItem);
                    nextId++;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PackageSaveTool] Skipped folder '{folderPath}': {e.Message}");
            }
        }

        private enum CheckState
        {
            Unchecked,
            Checked,
            Mixed
        }

        private static CheckState GetCheckState(FolderTreeItem item)
        {
            if (item.isMixed)
                return CheckState.Mixed;
            return item.isChecked ? CheckState.Checked : CheckState.Unchecked;
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
            EditorGUI.showMixedValue = state == CheckState.Mixed;
            bool newValue;
            try
            {
                newValue = EditorGUI.Toggle(toggleRect, displayValue);
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixedValue;
            }

            if (newValue != displayValue)
            {
                SetCheckedRecursively(item, newValue);
                UpdateAncestorStates(item);
                Repaint();
            }

            rowRect.x += indent + 24;
            rowRect.width -= indent + 24;

            var newArgs = args;
            newArgs.rowRect = rowRect;
            base.RowGUI(newArgs);
        }

        private static void SetCheckedRecursively(FolderTreeItem item, bool value)
        {
            item.isChecked = value;
            item.isMixed = false;
            if (!item.hasChildren || item.children == null)
                return;

            foreach (TreeViewItem child in item.children)
            {
                if (child is FolderTreeItem folderChild)
                    SetCheckedRecursively(folderChild, value);
            }
        }

        private static void UpdateAncestorStates(FolderTreeItem item)
        {
            var parent = item.parent as FolderTreeItem;
            while (parent != null)
            {
                ComputeFolderState(parent);
                parent = parent.parent as FolderTreeItem;
            }
        }

        private static void ComputeFolderState(FolderTreeItem folder)
        {
            if (!folder.hasChildren || folder.children == null || folder.children.Count == 0)
            {
                folder.isMixed = false;
                return;
            }

            bool anyChecked = false;
            bool anyUnchecked = false;
            foreach (TreeViewItem child in folder.children)
            {
                if (!(child is FolderTreeItem folderChild))
                    continue;

                if (folderChild.isMixed)
                {
                    anyChecked = true;
                    anyUnchecked = true;
                }
                else if (folderChild.isChecked)
                {
                    anyChecked = true;
                }
                else
                {
                    anyUnchecked = true;
                }
            }

            folder.isMixed = anyChecked && anyUnchecked;
            folder.isChecked = anyChecked && !anyUnchecked;
        }

        public List<string> GetSelectedPaths()
        {
            var result = new List<string>();
            if (rootItem != null)
                CollectCheckedTopLevel(rootItem, result);
            return result;
        }

        private static void CollectCheckedTopLevel(TreeViewItem item, List<string> result)
        {
            if (item is FolderTreeItem folder && folder.id != 0 && folder.isChecked && !folder.isMixed)
            {
                result.Add(folder.fullPath);
                return;
            }

            if (!item.hasChildren || item.children == null)
                return;

            foreach (TreeViewItem child in item.children)
                CollectCheckedTopLevel(child, result);
        }

        public void SetAllChecked(bool checkedValue)
        {
            foreach (var item in itemDict.Values)
            {
                item.isChecked = checkedValue;
                item.isMixed = false;
            }

            Repaint();
        }
    }
}
