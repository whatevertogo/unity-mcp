using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Manages snapshot cache metadata and timestamp-based change detection.
    /// </summary>
    public static class SnapshotCache
    {
        private const string CacheFileName = "Library/ProjectSnapshot/.cache";

        /// <summary>
        /// Gets the full path to the cache file.
        /// </summary>
        public static string GetCachePath(string basePath = null)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                basePath = Application.dataPath;
            }
            return Path.Combine(basePath, "..", CacheFileName);
        }

        /// <summary>
        /// Gets the full path to the cache file relative to project root.
        /// </summary>
        public static string GetRelativeCachePath()
        {
            return CacheFileName;
        }

        /// <summary>
        /// Gets the project's last modified time by checking all asset directories.
        /// </summary>
        public static DateTime GetProjectLastModified()
        {
            var assetsPath = Application.dataPath;
            var maxTime = File.GetLastWriteTime(assetsPath);

            try
            {
                // Check main directories
                var directoriesToCheck = Directory.GetDirectories(assetsPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(ShouldIncludeDirectoryForTimestamp);

                foreach (var dir in directoriesToCheck)
                {
                    try
                    {
                        var dirTime = Directory.GetLastWriteTime(dir);
                        if (dirTime > maxTime) maxTime = dirTime;

                        // Also check files in this directory
                        var files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly)
                            .Where(ShouldIncludeFileForTimestamp);

                        foreach (var file in files)
                        {
                            var fileTime = File.GetLastWriteTime(file);
                            if (fileTime > maxTime) maxTime = fileTime;
                        }
                    }
                    catch
                    {
                        // Skip directories we can't access
                    }
                }

                // Check files directly in Assets
                var assetFiles = Directory.GetFiles(assetsPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(ShouldIncludeFileForTimestamp);

                foreach (var file in assetFiles)
                {
                    var fileTime = File.GetLastWriteTime(file);
                    if (fileTime > maxTime) maxTime = fileTime;
                }
            }
            catch
            {
                // If timestamp check fails, return current time
                maxTime = DateTime.UtcNow;
            }

            return maxTime;
        }

        /// <summary>
        /// Reads the timestamp from a snapshot file.
        /// </summary>
        public static DateTime ReadSnapshotTimestamp(string snapshotPath)
        {
            var fullPath = Path.Combine(Application.dataPath, "..", snapshotPath);
            if (!File.Exists(fullPath)) return DateTime.MinValue;

            try
            {
                // Try to extract from the content first
                var content = File.ReadAllText(fullPath);
                var match = Regex.Match(content, @"\*Generated on (.+?) UTC\*");
                if (match.Success)
                {
                    if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var result))
                    {
                        return result;
                    }
                }

                // Fallback to file write time
                return File.GetLastWriteTime(fullPath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// Checks if the cache is valid based on the options.
        /// </summary>
        public static bool IsCacheValid(SnapshotOptions options)
        {
            if (!options.UseCache) return false;
            if (options.ForceRegenerate) return false;

            var cachePath = GetCachePath();
            if (!File.Exists(cachePath)) return false;

            try
            {
                var metadata = LoadMetadata(cachePath);
                if (metadata == null) return false;

                // Check cache version
                if (metadata.CacheVersion != 1) return false;

                // Check if project has been modified
                var projectModified = GetProjectLastModified();
                var cacheModifiedTime = new DateTime(metadata.ProjectLastModifiedTicks);

                if (projectModified > cacheModifiedTime)
                {
                    return false;
                }

                // Check cache validity duration
                if (options.CacheValidityMinutes > 0)
                {
                    var cacheAge = DateTime.UtcNow - metadata.LastGenerated;
                    if (cacheAge.TotalMinutes > options.CacheValidityMinutes)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads cache metadata from disk.
        /// </summary>
        public static SnapshotCacheMetadata LoadMetadata(string cachePath = null)
        {
            cachePath ??= GetCachePath();

            if (!File.Exists(cachePath)) return null;

            try
            {
                var json = File.ReadAllText(cachePath);
                return JsonConvert.DeserializeObject<SnapshotCacheMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves cache metadata to disk.
        /// </summary>
        public static void SaveMetadata(SnapshotCacheMetadata metadata, string cachePath = null)
        {
            cachePath ??= GetCachePath();

            try
            {
                metadata.LastModifiedCheck = DateTime.UtcNow;

                var json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
                var directory = Path.GetDirectoryName(cachePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(cachePath, json);
            }
            catch
            {
                // Silently fail - cache is optional
            }
        }

        /// <summary>
        /// Creates new cache metadata with current project state.
        /// </summary>
        public static SnapshotCacheMetadata CreateMetadata(SnapshotOptions options)
        {
            return new SnapshotCacheMetadata
            {
                LastGenerated = DateTime.UtcNow,
                LastModifiedCheck = DateTime.UtcNow,
                ProjectLastModifiedTicks = GetProjectLastModified().Ticks,
                HasDependencies = options.IncludeDependencies || options.SeparateDependenciesFile,
                TotalAssets = 0, // Will be updated during generation
                TotalPrefabs = 0, // Will be updated during generation
                CacheVersion = 1
            };
        }

        /// <summary>
        /// Checks if the project needs snapshot regeneration.
        /// </summary>
        public static ProjectDirtyCheckResult CheckProjectDirty(string snapshotPath)
        {
            var snapshotTime = ReadSnapshotTimestamp(snapshotPath);
            var projectModified = GetProjectLastModified();
            var isDirty = projectModified > snapshotTime;

            var result = new ProjectDirtyCheckResult
            {
                IsDirty = isDirty,
                LastProjectModified = projectModified,
                LastSnapshotGenerated = snapshotTime,
                SnapshotAgeMinutes = (DateTime.UtcNow - snapshotTime).TotalMinutes
            };

            if (isDirty)
            {
                result.Recommendation = "Project has changed since last snapshot. Consider regenerating.";
                result.ChangedAreas = DetectChangedAreas(snapshotTime, projectModified);
            }
            else
            {
                result.Recommendation = "Snapshot is up to date. No regeneration needed.";
            }

            return result;
        }

        /// <summary>
        /// Attempts to detect which areas of the project have changed.
        /// </summary>
        private static System.Collections.Generic.List<string> DetectChangedAreas(DateTime since, DateTime until)
        {
            var changedAreas = new System.Collections.Generic.List<string>();
            var assetsPath = Application.dataPath;

            try
            {
                var directories = Directory.GetDirectories(assetsPath, "*", SearchOption.TopDirectoryOnly)
                    .Where(ShouldIncludeDirectoryForTimestamp);

                foreach (var dir in directories)
                {
                    try
                    {
                        var dirTime = Directory.GetLastWriteTime(dir);
                        if (dirTime > since && dirTime <= until)
                        {
                            changedAreas.Add(Path.GetFileName(dir));
                        }
                    }
                    catch
                    {
                        // Skip inaccessible directories
                    }
                }
            }
            catch
            {
                // If detection fails, return empty list
            }

            return changedAreas;
        }

        /// <summary>
        /// Determines if a directory should be included in timestamp checks.
        /// </summary>
        private static bool ShouldIncludeDirectoryForTimestamp(string dirPath)
        {
            var dirName = new DirectoryInfo(dirPath).Name;
            return !dirName.In("Library", "Temp", "obj", ".git");
        }

        /// <summary>
        /// Determines if a file should be included in timestamp checks.
        /// </summary>
        private static bool ShouldIncludeFileForTimestamp(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return !fileName.StartsWith(".") &&
                   !filePath.Contains(".meta") &&
                   !filePath.EndsWith(".cs"); // Script changes don't always require snapshot update
        }

        /// <summary>
        /// Clears the cache by deleting the cache file.
        /// </summary>
        public static void ClearCache()
        {
            var cachePath = GetCachePath();
            try
            {
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
            }
            catch
            {
                // Silently fail
            }
        }
    }
}
