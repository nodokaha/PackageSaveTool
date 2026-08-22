using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace PackageSaveTool
{
    internal readonly struct DiffFilePair
    {
        public readonly string RelativePath;
        public readonly string SourcePath;
        public readonly string DestinationPath;

        public DiffFilePair(string relativePath, string sourcePath, string destinationPath)
        {
            RelativePath = relativePath;
            SourcePath = sourcePath;
            DestinationPath = destinationPath;
        }
    }

    internal static class FolderDiffer
    {
        private const int MaxPropertyCompareBytes = 2 * 1024 * 1024;
        private static readonly HashSet<string> ComparableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mat", ".prefab", ".asset", ".json", ".controller", ".anim", ".overrideController"
        };

        public static DetailedFolderDiffInfo CompareFolders(string sourceFolder, string destinationFolder, bool includeDeletes)
        {
            var sourceFiles = MapByRelativePath(sourceFolder);
            var destFiles = MapByRelativePath(destinationFolder);

            var pairs = sourceFiles.Keys.Union(destFiles.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(relPath =>
                {
                    sourceFiles.TryGetValue(relPath, out string src);
                    destFiles.TryGetValue(relPath, out string dst);
                    return new DiffFilePair(relPath, src, dst);
                });

            return ComparePairs(pairs, includeDeletes);
        }

        public static DetailedFolderDiffInfo ComparePairs(IEnumerable<DiffFilePair> pairs, bool includeDeletes)
        {
            var result = new DetailedFolderDiffInfo();
            if (pairs == null)
                return result;

            foreach (var pair in pairs)
            {
                if (string.IsNullOrEmpty(pair.RelativePath))
                    continue;

                bool inSource = !string.IsNullOrEmpty(pair.SourcePath) && File.Exists(pair.SourcePath);
                bool inDest = !string.IsNullOrEmpty(pair.DestinationPath) && File.Exists(pair.DestinationPath);

                if (inSource && !inDest)
                {
                    result.FileDetails.Add(new FileDiffDetail
                    {
                        RelativePath = pair.RelativePath,
                        Status = DiffStatus.Added
                    });
                }
                else if (!inSource && inDest)
                {
                    if (!includeDeletes)
                        continue;

                    result.FileDetails.Add(new FileDiffDetail
                    {
                        RelativePath = pair.RelativePath,
                        Status = DiffStatus.Removed
                    });
                }
                else if (inSource && inDest && !FileContentsEqual(pair.SourcePath, pair.DestinationPath))
                {
                    var detail = new FileDiffDetail
                    {
                        RelativePath = pair.RelativePath,
                        Status = DiffStatus.Modified
                    };

                    if (IsComparableAssetFile(pair.SourcePath))
                        detail.PropertyDiffs = CompareProperties(pair.DestinationPath, pair.SourcePath);

                    result.FileDetails.Add(detail);
                }
            }

            return result;
        }

        public static bool FileContentsEqual(string file1, string file2)
        {
            try
            {
                var info1 = new FileInfo(file1);
                var info2 = new FileInfo(file2);
                if (!info1.Exists || !info2.Exists)
                    return false;
                if (info1.Length != info2.Length)
                    return false;
                if (info1.Length == 0)
                    return true;

                const int bufferSize = 81920;
                var buffer1 = new byte[bufferSize];
                var buffer2 = new byte[bufferSize];

                using (var stream1 = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var stream2 = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    while (true)
                    {
                        int read1 = stream1.Read(buffer1, 0, bufferSize);
                        int read2 = stream2.Read(buffer2, 0, bufferSize);
                        if (read1 != read2)
                            return false;
                        if (read1 == 0)
                            return true;

                        for (int i = 0; i < read1; i++)
                        {
                            if (buffer1[i] != buffer2[i])
                                return false;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PackageSaveTool] File compare failed ({file1}): {e.Message}");
                return false;
            }
        }

        public static List<string> GetAllFiles(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return new List<string>();

            return Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static Dictionary<string, string> MapByRelativePath(string folderPath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in GetAllFiles(folderPath))
            {
                string rel = PathUtil.GetRelativePath(file, folderPath);
                if (!string.IsNullOrEmpty(rel) && !map.ContainsKey(rel))
                    map.Add(rel, file);
            }

            return map;
        }

        private static bool IsComparableAssetFile(string filePath)
        {
            return ComparableExtensions.Contains(Path.GetExtension(filePath));
        }

        private static List<PropertyDiffItem> CompareProperties(string oldFilePath, string newFilePath)
        {
            try
            {
                if (new FileInfo(oldFilePath).Length > MaxPropertyCompareBytes ||
                    new FileInfo(newFilePath).Length > MaxPropertyCompareBytes)
                {
                    return new List<PropertyDiffItem>();
                }

                var oldProps = ParseYamlProperties(oldFilePath);
                var newProps = ParseYamlProperties(newFilePath);
                var diffs = new List<PropertyDiffItem>();

                foreach (var kv in newProps)
                {
                    if (oldProps.TryGetValue(kv.Key, out string oldValue))
                    {
                        if (oldValue != kv.Value)
                        {
                            diffs.Add(new PropertyDiffItem
                            {
                                PropertyName = kv.Key,
                                OldValue = oldValue,
                                NewValue = kv.Value
                            });
                        }
                    }
                    else
                    {
                        diffs.Add(new PropertyDiffItem
                        {
                            PropertyName = kv.Key,
                            OldValue = "(なし)",
                            NewValue = kv.Value
                        });
                    }
                }

                foreach (var kv in oldProps)
                {
                    if (!newProps.ContainsKey(kv.Key))
                    {
                        diffs.Add(new PropertyDiffItem
                        {
                            PropertyName = kv.Key,
                            OldValue = kv.Value,
                            NewValue = "(削除)"
                        });
                    }
                }

                return diffs;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PackageSaveTool] Failed to compare properties: {e.Message}");
                return new List<PropertyDiffItem>();
            }
        }

        private static Dictionary<string, string> ParseYamlProperties(string filePath)
        {
            var props = new Dictionary<string, string>();
            string currentContext = "";

            foreach (var line in File.ReadLines(filePath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;

                if (trimmed.StartsWith("--- !u!", StringComparison.Ordinal))
                {
                    currentContext = trimmed;
                    continue;
                }

                int colonIndex = trimmed.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                string key = trimmed.Substring(0, colonIndex).Trim();
                string val = trimmed.Substring(colonIndex + 1).Trim();
                if (string.IsNullOrEmpty(val) || key.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string fullKey = string.IsNullOrEmpty(currentContext) ? key : $"{currentContext} -> {key}";
                props[fullKey] = val;
            }

            return props;
        }
    }
}
