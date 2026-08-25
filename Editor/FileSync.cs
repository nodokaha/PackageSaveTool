using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace PackageSaveTool
{
    internal sealed class EditorProgress : IDisposable
    {
        private readonly string _title;

        public EditorProgress(string title)
        {
            _title = title;
        }

        public void Report(string info, float progress)
        {
            EditorUtility.DisplayProgressBar(_title, info, progress);
        }

        public void Dispose()
        {
            EditorUtility.ClearProgressBar();
        }
    }

    internal sealed class AssetEditingScope : IDisposable
    {
        public AssetEditingScope()
        {
            AssetDatabase.StartAssetEditing();
        }

        public void Dispose()
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    internal static class FileSync
    {
        public static void CopyFileWithMeta(string sourcePath, string destPath)
        {
            string destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            CopyOverwrite(sourcePath, destPath);

            string metaSource = sourcePath + ".meta";
            if (File.Exists(metaSource))
                CopyOverwrite(metaSource, destPath + ".meta");
        }

        public static void CopyOverwrite(string sourcePath, string destPath)
        {
            if (File.Exists(destPath))
            {
                var attrs = File.GetAttributes(destPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(destPath, attrs & ~FileAttributes.ReadOnly);
            }

            File.Copy(sourcePath, destPath, true);
        }

        public static void DeleteFileWithMeta(string path)
        {
            FileUtil.DeleteFileOrDirectory(path);
            FileUtil.DeleteFileOrDirectory(path + ".meta");
        }

        public static void CopyDirectory(string sourcePath, string destPath, bool merge)
        {
            if (!merge && Directory.Exists(destPath))
            {
                FileUtil.DeleteFileOrDirectory(destPath);
                FileUtil.DeleteFileOrDirectory(destPath + ".meta");
                if (Directory.Exists(destPath))
                    Directory.Delete(destPath, true);
            }

            Directory.CreateDirectory(destPath);

            foreach (var file in Directory.GetFiles(sourcePath))
            {
                CopyOverwrite(file, Path.Combine(destPath, Path.GetFileName(file)));
            }

            foreach (var folder in Directory.GetDirectories(sourcePath))
            {
                CopyDirectory(folder, Path.Combine(destPath, Path.GetFileName(folder)), merge);
            }
        }

        public static void RemoveOrphanFiles(string destRoot, IEnumerable<string> relativePathsToKeep, string extraKeepFileName)
        {
            if (!Directory.Exists(destRoot))
                return;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (relativePathsToKeep != null)
            {
                foreach (var rel in relativePathsToKeep)
                {
                    if (string.IsNullOrEmpty(rel))
                        continue;
                    keep.Add(PathUtil.NormalizeSlashes(rel));
                }
            }

            if (!string.IsNullOrEmpty(extraKeepFileName))
                keep.Add(PathUtil.NormalizeSlashes(extraKeepFileName));

            foreach (var destFile in Directory.GetFiles(destRoot, "*", SearchOption.AllDirectories))
            {
                if (destFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string rel = PathUtil.GetRelativePath(destFile, destRoot);
                if (keep.Contains(rel))
                    continue;

                DeleteFileWithMeta(destFile);
            }

            DeleteEmptyDirectories(destRoot, destRoot);
        }

        private static void DeleteEmptyDirectories(string current, string root)
        {
            if (!Directory.Exists(current))
                return;

            foreach (var dir in Directory.GetDirectories(current))
                DeleteEmptyDirectories(dir, root);

            if (!PathsEqual(current, root) &&
                Directory.GetFiles(current).Length == 0 &&
                Directory.GetDirectories(current).Length == 0)
            {
                Directory.Delete(current);
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            string na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }
    }
}
