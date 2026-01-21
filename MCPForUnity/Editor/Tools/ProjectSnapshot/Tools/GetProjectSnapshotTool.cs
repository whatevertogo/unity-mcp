using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Gets the Unity project snapshot with tiered detail levels.
    /// This is the primary tool for understanding project structure.
    /// </summary>
    [McpForUnityTool("get_project_snapshot",
        Description = "Get Unity project snapshot with tiered detail levels. Basic=overview, Structure=entry points & systems, Verbose=full context.")]
    public static class GetProjectSnapshotTool
    {
        /// <summary>
        /// Parameters for get_project_snapshot tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Detail level: basic (overview), structure (entry points & systems), verbose (full context).
            /// </summary>
            [ToolParameter("Detail level: basic (overview), structure (entry points & systems), verbose (full context). Default: basic",
                Required = false, DefaultValue = "basic")]
            public string DetailLevel { get; set; } = "basic";

            /// <summary>
            /// Force regeneration of snapshot (slower, ensures fresh data).
            /// </summary>
            [ToolParameter("Force regeneration of snapshot (slower, ensures fresh data). Default: false",
                Required = false, DefaultValue = "false")]
            public bool ForceRefresh { get; set; } = false;

            /// <summary>
            /// Include dependencies file content (can be large, only for verbose level).
            /// </summary>
            [ToolParameter("Include dependencies file content (can be large). Default: false",
                Required = false, DefaultValue = "false")]
            public bool IncludeDependencies { get; set; } = false;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var detailLevelStr = @params["detail_level"]?.ToString()?.ToLower() ?? "basic";
                var forceRefresh = @params["force_refresh"]?.Value<bool>() ?? false;
                var includeDependencies = @params["include_dependencies"]?.Value<bool>() ?? false;

                // Parse detail level
                if (!Enum.TryParse<DetailLevel>(detailLevelStr, true, out var detailLevel))
                {
                    // Map string values to enum
                    detailLevel = detailLevelStr switch
                    {
                        "basic" => DetailLevel.Basic,
                        "structure" => DetailLevel.Structure,
                        "verbose" => DetailLevel.Verbose,
                        _ => DetailLevel.Basic
                    };
                }

                var (settings, options) = SnapshotHelpers.GetSettingsAndOptions();

                // Force refresh if requested
                if (forceRefresh)
                {
                    ProjectSnapshotGenerator.GenerateNow(options);
                }

                // Try to load structured snapshot first
                var snapshotData = ProjectSnapshotGenerator.GenerateStructuredSnapshot(options, forceRegenerate: forceRefresh);

                if (snapshotData == null)
                {
                    return new ErrorResponse(
                        "No snapshot found. It will be auto-generated after script compilation. " +
                        $"Expected location: {options.OutputPath}"
                    );
                }

                // Get content based on detail level
                var content = snapshotData.GetContent(detailLevel);

                // Build metadata response
                var metadata = new Dictionary<string, object>
                {
                    ["generated_at"] = snapshotData.Metadata.GeneratedAt,
                    ["unity_version"] = snapshotData.Metadata.UnityVersion,
                    ["render_pipeline"] = snapshotData.Metadata.RenderPipeline,
                    ["project_name"] = snapshotData.Metadata.ProjectName,
                    ["age_minutes"] = Math.Round(snapshotData.Metadata.AgeMinutes, 1),
                    ["is_compiling"] = snapshotData.Metadata.IsCompiling,
                    ["script_count"] = snapshotData.Metadata.ScriptCount,
                    ["scene_count"] = snapshotData.Metadata.SceneCount
                };

                // Add dependencies if requested (only for verbose level)
                object dependenciesContent = null;
                if (includeDependencies && detailLevel == DetailLevel.Verbose)
                {
                    dependenciesContent = SnapshotHelpers.ReadSnapshotContentSafe(options.DependenciesOutputPath);
                }

                // Calculate size info
                var sizeInfo = detailLevel switch
                {
                    DetailLevel.Basic => "~500 chars",
                    DetailLevel.Structure => "~3K chars",
                    DetailLevel.Verbose => $"~{content.Length} chars",
                    _ => $"{content.Length} chars"
                };

                return new SuccessResponse(
                    $"Snapshot loaded ({sizeInfo}, generated {snapshotData.Metadata.AgeMinutes:F0} min ago)",
                    new Dictionary<string, object>
                    {
                        ["metadata"] = metadata,
                        ["content"] = content,
                        ["dependencies_content"] = dependenciesContent,
                        ["char_count"] = content.Length
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error reading snapshot: {e.Message}");
            }
        }
    }
}
