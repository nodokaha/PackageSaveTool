using System;
using System.Collections.Generic;

namespace PackageSaveTool
{
    /// <summary>
    /// セマンティックバージョン情報
    /// </summary>
    [Serializable]
    public class VersionInfo : IComparable<VersionInfo>
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

        public VersionInfo Clone()
        {
            return new VersionInfo(major, minor, patch);
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

        public int CompareTo(VersionInfo other)
        {
            if (other == null)
                return 1;

            int cmp = major.CompareTo(other.major);
            if (cmp != 0) return cmp;
            cmp = minor.CompareTo(other.minor);
            if (cmp != 0) return cmp;
            return patch.CompareTo(other.patch);
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

    public static class DiffStatus
    {
        public const string Added = "新規追加";
        public const string Removed = "削除";
        public const string Modified = "変更あり";
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
        public bool IsFixed;
        public string Note;
    }

    public class PropertyDiffItem
    {
        public string PropertyName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    public class FileDiffDetail
    {
        public string RelativePath { get; set; }
        public string Status { get; set; }
        public List<PropertyDiffItem> PropertyDiffs { get; set; } = new List<PropertyDiffItem>();
    }

    public class DetailedFolderDiffInfo
    {
        public List<FileDiffDetail> FileDetails { get; } = new List<FileDiffDetail>();
    }
}
