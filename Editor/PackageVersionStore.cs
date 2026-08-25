using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PackageSaveTool
{
    internal static class PackageVersionStore
    {
        private static readonly Regex VersionSuffixRegex = new Regex(
            @"[_=]([vV]?\d+\.\d+\.\d+)$",
            RegexOptions.Compiled);

        private static readonly Regex VersionedWrapperRegex = new Regex(
            @"^.+(?:[_=])[vV]?\d+\.\d+\.\d+$",
            RegexOptions.Compiled);

        private static string ProjectVersionFile
        {
            get
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    return Path.Combine(Application.persistentDataPath, "PackageSaveTool_version_info.json");

                return Path.Combine(projectRoot, "Library", "PackageSaveTool", "version_info.json");
            }
        }

        private static string LegacyVersionFile =>
            Path.Combine(Application.persistentDataPath, "version_info.json");

        public static VersionInfo Load()
        {
            VersionInfo loaded = TryRead(ProjectVersionFile);
            if (loaded != null)
                return loaded;

            loaded = TryRead(LegacyVersionFile);
            if (loaded != null)
            {
                Save(loaded);
                return loaded;
            }

            return new VersionInfo(1, 0, 0);
        }

        public static void Save(VersionInfo version)
        {
            if (version == null)
                return;

            try
            {
                string path = ProjectVersionFile;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, JsonUtility.ToJson(version, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PackageSaveTool] Failed to save version info: {e.Message}");
            }
        }

        public static VersionInfo ExtractFromFolderName(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return null;

            string folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var match = VersionSuffixRegex.Match(folderName);
            if (!match.Success)
                return null;

            string versionStr = match.Groups[1].Value;
            if (versionStr.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                versionStr = versionStr.Substring(1);

            var parts = versionStr.Split('.');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out int major) &&
                int.TryParse(parts[1], out int minor) &&
                int.TryParse(parts[2], out int patch))
            {
                return new VersionInfo(major, minor, patch);
            }

            return null;
        }

        public static bool IsVersionedWrapperFolder(string folderName)
        {
            return !string.IsNullOrEmpty(folderName) && VersionedWrapperRegex.IsMatch(folderName);
        }

        public static string GetImportSourceFolder(string folderPath)
        {
            string folderName = Path.GetFileName(folderPath);
            if (IsVersionedWrapperFolder(folderName))
            {
                var childDirs = Directory.GetDirectories(folderPath);
                if (childDirs.Length > 0)
                {
                    Array.Sort(childDirs, StringComparer.OrdinalIgnoreCase);
                    return childDirs[0];
                }
            }

            return folderPath;
        }

        private static VersionInfo TryRead(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json) || json.IndexOf("major", StringComparison.OrdinalIgnoreCase) < 0)
                    return null;

                return JsonUtility.FromJson<VersionInfo>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PackageSaveTool] Failed to read version info ({path}): {e.Message}");
                return null;
            }
        }
    }
}
