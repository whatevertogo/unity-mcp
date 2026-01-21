using System;
using Newtonsoft.Json.Linq;
using UnityEditor;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Tool to manually regenerate the project snapshot.
    /// Use this to force a snapshot update without waiting for auto-generation.
    /// </summary>
    [McpForUnityTool("regenerate_project_snapshot",
        Description = "Force regenerate the project snapshot. Use when auto-generation is disabled or you need fresh data immediately.")]
    public static class RegenerateSnapshotTool
    {
        public class Parameters
        {
            [ToolParameter("Generate dependency index as well. Default: true",
                Required = false, DefaultValue = "true")]
            public bool GenerateIndex { get; set; } = true;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var generateIndex = @params["generate_index"]?.Value<bool>() ?? true;

                var (settings, options) = SnapshotHelpers.GetSettingsAndOptions();
                options.GenerateIndex = generateIndex;

                // Force regeneration
                McpLog.Info("[RegenerateSnapshot] Starting manual snapshot generation...");
                var result = ProjectSnapshotGenerator.GenerateNow(options);

                if (result != null && result.Success)
                {
                    McpLog.Info($"[RegenerateSnapshot] Snapshot generated successfully in {result.GenerationTimeMs}ms. Output: {result.OutputPath}");
                    return new SuccessResponse(
                        $"Snapshot regenerated in {result.GenerationTimeMs}ms",
                        new
                        {
                            output_path = result.OutputPath,
                            dependencies_path = result.DependenciesPath,
                            word_count = result.WordCount,
                            generation_time_ms = result.GenerationTimeMs,
                            index_generated = result.IndexGenerated
                        }
                    );
                }
                else if (result == null)
                {
                    return new ErrorResponse("Generation skipped (already in progress). Try again later.");
                }
                else
                {
                    return new ErrorResponse($"Generation failed: {result.ErrorMessage}");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error regenerating snapshot: {e.Message}");
            }
        }
    }
}
