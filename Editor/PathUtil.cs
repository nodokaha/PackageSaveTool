using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PackageSaveTool
{
    internal static class PathUtil
    {
        public static string CombineUnder(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return root;

            string normalizedRel = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(root, normalizedRel);
        }

        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (Array.IndexOf(invalidChars, c) < 0)
                    sanitized.Append(c);
            }

            return sanitized.ToString().Trim();
        }

        public static string NormalizeSlashes(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>
        /// Unity アセットパス ("Assets/...") を Assets フォルダからの相対パスに変換する。
        /// 変換できない場合は null。
        /// </summary>
        public static string GetPathRelativeToAssets(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return null;

            string normalized = NormalizeSlashes(fullPath).TrimEnd('/');
            string assetsAbsolute = NormalizeSlashes(Application.dataPath).TrimEnd('/');

            if (normalized.StartsWith(assetsAbsolute + "/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(assetsAbsolute.Length + 1);

            if (normalized.Equals(assetsAbsolute, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            const string prefix = "Assets/";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(prefix.Length);

            if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return null;
        }

        public static string NormalizeToAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string normalized = NormalizeSlashes(path);
            string dataPath = NormalizeSlashes(Application.dataPath);

            if (normalized.Equals(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets";

            if (normalized.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
                return "Assets" + normalized.Substring(dataPath.Length);

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return null;
        }

        public static string GetRelativePath(string filePath, string rootFolder)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(rootFolder))
                return Path.GetFileName(filePath);

            string normalizedFile = NormalizeSlashes(filePath);
            string normalizedRoot = NormalizeSlashes(rootFolder).TrimEnd('/') + '/';

            if (normalizedFile.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return normalizedFile.Substring(normalizedRoot.Length);

            return Path.GetFileName(filePath);
        }

        public static string ToAssetPathFromRelative(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return "Assets";

            return "Assets/" + NormalizeSlashes(relativePath);
        }

        public static List<string> FilterTopLevelSelections(List<string> selectedPaths)
        {
            var filtered = new List<string>();
            if (selectedPaths == null || selectedPaths.Count == 0)
                return filtered;

            var normalizedPaths = selectedPaths
                .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .ToList();

            foreach (var path in normalizedPaths.OrderBy(p => p.Length))
            {
                if (!filtered.Any(parent => IsAncestor(parent, path)))
                    filtered.Add(path);
            }

            return filtered;
        }

        public static bool IsAncestor(string ancestorPath, string path)
        {
            if (string.Equals(ancestorPath, path, StringComparison.OrdinalIgnoreCase))
                return false;

            ancestorPath = ancestorPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(ancestorPath, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetSafeManifestPaths(
            string sourceRoot,
            string projectAssetsPath,
            string relativePath,
            out string sourcePath,
            out string destinationPath)
        {
            sourcePath = null;
            destinationPath = null;

            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                return false;

            if (relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return false;

            string normalizedRel = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string fullSourceRoot = AppendSeparator(Path.GetFullPath(sourceRoot));
            string fullAssetsRoot = AppendSeparator(Path.GetFullPath(projectAssetsPath));
            string fullSourcePath = Path.GetFullPath(Path.Combine(fullSourceRoot, normalizedRel));
            string fullDestinationPath = Path.GetFullPath(Path.Combine(fullAssetsRoot, normalizedRel));

            if (!IsInsideRoot(fullSourcePath, fullSourceRoot) || !IsInsideRoot(fullDestinationPath, fullAssetsRoot))
                return false;

            sourcePath = fullSourcePath;
            destinationPath = fullDestinationPath;
            return true;
        }

        private static string AppendSeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static bool IsInsideRoot(string fullPath, string rootWithSeparator)
        {
            string pathWithSeparator = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return pathWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
    }
}
