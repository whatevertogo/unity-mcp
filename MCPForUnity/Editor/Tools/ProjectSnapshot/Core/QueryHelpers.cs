using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    internal static class QueryHelpers
    {
        private static readonly object _indexLock = new object();
        private static DependencyIndex _index = null;

        public static DependencyIndex Index
        {
            get { lock (_indexLock) { return _index; } }
            private set { lock (_indexLock) { _index = value; } }
        }

        public static bool EnsureIndexLoaded(bool autoGenerate)
        {
            lock (_indexLock)
            {
                if (_index != null && _index.IsLoaded) return true;
                var index = new DependencyIndex();
                var indexPath = DependencyIndex.GetDefaultIndexPath();
                if (index.LoadIndex(indexPath)) { _index = index; return true; }
                if (autoGenerate)
                {
                    try
                    {
                        var options = new SnapshotOptions();
                        index.GenerateIndex(options);
                        index.SaveIndex(indexPath);
                        _index = index;
                        return true;
                    }
                    catch { return false; }
                }
                return false;
            }
        }

        public static DependencyQueryResult GetDirectDependencies(string assetPath, bool includeDependents)
        {
            if (!Utilities.AssetExists(assetPath)) return null;
            var deps = AssetDatabase.GetDependencies(assetPath, recursive: false);
            var result = new DependencyQueryResult
            {
                AssetPath = assetPath,
                AssetType = Utilities.GetAssetTypeFromPath(assetPath),
                FromCache = false
            };
            foreach (var dep in deps)
            {
                if (dep == assetPath) continue;
                result.Dependencies.Add(new DependencyInfo
                {
                    Path = dep,
                    Type = Utilities.GetAssetTypeFromPath(dep),
                    Guid = AssetDatabase.AssetPathToGUID(dep)
                });
            }
            result.TotalDependencies = result.Dependencies.Count;
            if (includeDependents)
            {
                var allPrefabs = AssetDatabase.FindAssets("t:Prefab");
                var dependents = new List<string>();
                foreach (var guid in allPrefabs.Take(100))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path == assetPath) continue;
                    try
                    {
                        var depsOf = AssetDatabase.GetDependencies(path, recursive: false);
                        if (depsOf.Contains(assetPath)) dependents.Add(path);
                    }
                    catch { }
                }
                foreach (var dep in dependents)
                {
                    result.Dependents.Add(new DependencyInfo
                    {
                        Path = dep,
                        Type = "Prefab",
                        Guid = AssetDatabase.AssetPathToGUID(dep)
                    });
                }
                result.TotalDependents = result.Dependents.Count;
            }
            return result;
        }

        public static List<Dictionary<string, object>> GenerateDirectoryTree(
            string rootPath, int currentDepth, int maxDepth, SnapshotOptions options, bool includeFileCounts)
        {
            var result = new List<Dictionary<string, object>>();
            if (currentDepth > maxDepth) return result;

            var fullPath = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullPath)) return result;

            var dirName = new DirectoryInfo(fullPath).Name;
            if (!options.IncludePackages &&
                (dirName == "Library" || dirName == "Temp" || dirName == "obj" ||
                 dirName == ".git" || dirName == "Packages" || dirName == "ProjectSettings"))
                return result;

            var dirs = Directory.GetDirectories(fullPath)
                .Where(d => options.IncludePackages ||
                           !new DirectoryInfo(d).Name.In("Library", "Temp", "obj", ".git", "Packages"))
                .OrderBy(d => new DirectoryInfo(d).Name)
                .ToList();

            var files = Directory.GetFiles(fullPath)
                .Where(f => !new FileInfo(f).Name.StartsWith(".") && !f.Contains(".meta"))
                .ToList();

            var entry = new Dictionary<string, object>
            {
                ["name"] = dirName,
                ["path"] = rootPath.Replace('\\', '/'),
                ["depth"] = currentDepth
            };

            var comment = Utilities.GetDirectoryComment(dirName, files, dirs);
            if (!string.IsNullOrEmpty(comment)) entry["comment"] = comment;

            if (includeFileCounts)
            {
                entry["subdirectory_count"] = dirs.Count;
                entry["file_count"] = files.Count;
            }

            if (dirs.Count > 0)
            {
                entry["subdirectories"] = new List<Dictionary<string, object>>();
                foreach (var dir in dirs)
                {
                    var relativePath = rootPath + "/" + new DirectoryInfo(dir).Name;
                    var subEntries = GenerateDirectoryTree(relativePath, currentDepth + 1, maxDepth, options, includeFileCounts);
                    if (subEntries.Count > 0)
                        ((List<Dictionary<string, object>>)entry["subdirectories"]).AddRange(subEntries);
                }
            }

            result.Add(entry);
            return result;
        }

        public static Dictionary<string, object> CalculateDirectoryStats(string rootPath, bool includePackages)
        {
            var fullPath = Path.GetFullPath(rootPath);
            var stats = new Dictionary<string, object>();

            try
            {
                var allDirs = Directory.GetDirectories(fullPath, "*", SearchOption.AllDirectories)
                    .Where(d => includePackages || !new DirectoryInfo(d).Name.In("Library", "Temp", "obj", ".git", "Packages"))
                    .ToList();

                var allFiles = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories)
                    .Where(f => !new FileInfo(f).Name.StartsWith(".") && !f.Contains(".meta"))
                    .ToList();

                stats["total_directories"] = allDirs.Count;
                stats["total_files"] = allFiles.Count;

                var extCounts = new Dictionary<string, int>();
                foreach (var file in allFiles)
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (string.IsNullOrEmpty(ext)) ext = "no_extension";
                    if (extCounts.ContainsKey(ext))
                        extCounts[ext]++;
                    else
                        extCounts[ext] = 1;
                }

                stats["file_types"] = extCounts.OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            catch
            {
                stats["error"] = "Could not calculate full statistics";
            }

            return stats;
        }
    }
}
