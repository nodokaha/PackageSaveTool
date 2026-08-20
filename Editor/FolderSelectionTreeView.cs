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
}