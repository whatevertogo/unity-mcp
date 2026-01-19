using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    using MCPForUnity.Editor.Helpers;

    #region CheckProjectDirtyTool

    /// <summary>
    /// Checks if project has changed since last snapshot using lightweight timestamp comparison.
    /// Use before regenerating snapshots to avoid unnecessary work.
    /// </summary>
    [McpForUnityTool("check_project_dirty", Description = "Checks if project has changed since last snapshot using lightweight timestamp comparison. Use before regenerating snapshots to avoid unnecessary work.")]
    public static class CheckProjectDirtyTool
    {
        /// <summary>
        /// Parameters for check_project_dirty tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Path to the snapshot file to compare against.
            /// </summary>
            [ToolParameter("Path to the snapshot file to compare against (default: Project_Snapshot.md)", Required = false, DefaultValue = "Project_Snapshot.md")]
            public string SnapshotPath { get; set; } = "Project_Snapshot.md";
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var snapshotPath = @params["snapshot_path"]?.ToString();
                if (string.IsNullOrEmpty(snapshotPath))
                {
                    snapshotPath = "Project_Snapshot.md";
                }

                var result = SnapshotCache.CheckProjectDirty(snapshotPath);

                return new SuccessResponse("Dirty check complete.", new
                {
                    is_dirty = result.IsDirty,
                    last_project_modified = result.LastProjectModified.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    last_snapshot_generated = result.LastSnapshotGenerated.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    snapshot_age_minutes = Math.Round(result.SnapshotAgeMinutes, 2),
                    recommendation = result.Recommendation,
                    changed_areas = result.ChangedAreas.Count > 0 ? result.ChangedAreas.ToArray() : Array.Empty<string>()
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error checking project dirty status: {e.Message}");
            }
        }
    }

    #endregion

    #region ClearSnapshotCacheTool

    /// <summary>
    /// Clears the snapshot cache, forcing a full regeneration on next snapshot.
    /// </summary>
    [McpForUnityTool("clear_snapshot_cache", Description = "Clears the snapshot cache, forcing a full regeneration on next snapshot.")]
    public static class ClearSnapshotCacheTool
    {
        /// <summary>
        /// Parameters for clear_snapshot_cache tool.
        /// </summary>
        public class Parameters
        {
            // This tool has no parameters - all operations are unconditional
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                SnapshotCache.ClearCache();

                // Also clear index if it exists
                var indexPath = DependencyIndex.GetDefaultIndexPath();
                if (System.IO.File.Exists(indexPath))
                {
                    System.IO.File.Delete(indexPath);
                }

                return new SuccessResponse("Snapshot cache cleared.", new
                {
                    cache_cleared = true,
                    index_cleared = true,
                    recommendation = "Next snapshot generation will perform a full analysis."
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error clearing cache: {e.Message}");
            }
        }
    }

    #endregion

    #region GetCacheStatusTool

    /// <summary>
    /// Returns the current snapshot cache status including metadata.
    /// </summary>
    [McpForUnityTool("get_cache_status", Description = "Returns the current snapshot cache status including metadata.")]
    public static class GetCacheStatusTool
    {
        /// <summary>
        /// Parameters for get_cache_status tool.
        /// </summary>
        public class Parameters
        {
            // This tool has no parameters - it simply returns the current status
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var metadata = SnapshotCache.LoadMetadata();
                var indexExists = System.IO.File.Exists(DependencyIndex.GetDefaultIndexPath());

                if (metadata == null)
                {
                    return new SuccessResponse("No cache found.", new
                    {
                        cache_exists = false,
                        index_exists = indexExists,
                        recommendation = "Generate a snapshot to create the cache."
                    });
                }

                var projectModified = new DateTime(metadata.ProjectLastModifiedTicks);
                var isDirty = projectModified > metadata.LastGenerated;

                return new SuccessResponse("Cache status retrieved.", new
                {
                    cache_exists = true,
                    index_exists = indexExists,
                    is_dirty = isDirty,
                    last_generated = metadata.LastGenerated.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    project_last_modified = projectModified.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    cache_version = metadata.CacheVersion,
                    has_dependencies = metadata.HasDependencies,
                    total_assets = metadata.TotalAssets,
                    total_prefabs = metadata.TotalPrefabs
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting cache status: {e.Message}");
            }
        }
    }

    #endregion

    #region RegenerateDependencyIndexTool

    /// <summary>
    /// Regenerates the dependency index for fast queries without generating the full snapshot.
    /// </summary>
    [McpForUnityTool("regenerate_dependency_index", Description = "Regenerates the dependency index for fast queries without generating the full snapshot.")]
    public static class RegenerateDependencyIndexTool
    {
        /// <summary>
        /// Parameters for regenerate_dependency_index tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Optional path to ProjectSnapshotSettings asset for configuration.
            /// </summary>
            [ToolParameter("Optional path to ProjectSnapshotSettings asset for configuration", Required = false)]
            public string SettingsAssetPath { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                SnapshotOptions options = null;

                // Try to load from settings
                var settingsPath = @params["settings_asset_path"]?.ToString();
                if (!string.IsNullOrEmpty(settingsPath))
                {
                    var settings = AssetDatabase.LoadAssetAtPath<ProjectSnapshotSettings>(settingsPath);
                    if (settings != null)
                    {
                        options = settings.ToOptions();
                    }
                }

                options ??= new SnapshotOptions();

                var index = new DependencyIndex();
                index.GenerateIndex(options);

                var indexPath = DependencyIndex.GetDefaultIndexPath();
                var success = index.SaveIndex(indexPath);

                if (success)
                {
                    return new SuccessResponse("Dependency index regenerated.", new
                    {
                        index_path = "Library/ProjectSnapshot/.index",
                        asset_count = index.Count,
                        last_generated = index.LastGenerated.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    });
                }
                else
                {
                    return new ErrorResponse("Failed to save dependency index.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error regenerating index: {e.Message}");
            }
        }
    }

    #endregion
}
