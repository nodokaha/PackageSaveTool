using System;
using System.Collections.Generic;

namespace PackageSaveTool
{
    /// <summary>
    /// セマンティックバージョン情報
    /// </summary>
    [Serializable]
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
    /// Save時に選択されたAssetsからの相対パス一覧を保持するマニフェスト
    /// </summary>
    [Serializable]
    public class SelectionManifest
    {
        public string[] relativePaths;
    }

    /// <summary>
    /// フォルダ差分情報
    /// </summary>
    public class FolderDiffInfo
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

    public class PropertyDiffItem
    {
        public string PropertyName { get; set; } // パラメータ名（キー）
        public string OldValue { get; set; }     // 変更前の値
        public string NewValue { get; set; }     // 変更後の値
    }

    public class FileDiffDetail
    {
        public string RelativePath { get; set; }
        public string Status { get; set; } // "追加", "削除", "パラメータ変更"
        public List<PropertyDiffItem> PropertyDiffs { get; set; } = new List<PropertyDiffItem>();
    }

    public class DetailedFolderDiffInfo
    {
        public List<FileDiffDetail> FileDetails { get; } = new List<FileDiffDetail>();
    }
}
