using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    using MCPForUnity.Editor.Helpers;

    #region GetProjectArchitectureTool

    /// <summary>
    /// Returns project architecture analysis including architecture type, entry points, loading strategy, and manager classes.
    /// Use when you need to understand the project structure without generating a full snapshot.
    /// </summary>
    [McpForUnityTool("get_project_architecture", Description = "Returns project architecture analysis including architecture type, entry points, loading strategy, and manager classes. Use when you need to understand the project structure without generating a full snapshot.")]
    public static class GetProjectArchitectureTool
    {
        /// <summary>
        /// Parameters for get_project_architecture tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Whether to include entry points in the result.
            /// </summary>
            [ToolParameter("Whether to include entry points in the result", Required = false, DefaultValue = "true")]
            public bool IncludeEntryPoints { get; set; } = true;

            /// <summary>
            /// Whether to include manager classes in the result.
            /// </summary>
            [ToolParameter("Whether to include manager classes in the result", Required = false, DefaultValue = "true")]
            public bool IncludeManagers { get; set; } = true;

            /// <summary>
            /// Maximum number of manager classes to return.
            /// </summary>
            [ToolParameter("Maximum number of manager classes to return", Required = false, DefaultValue = "20")]
            public int MaxManagers { get; set; } = 20;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var includeEntryPoints = @params["include_entry_points"]?.Value<bool>() ?? true;
                var includeManagers = @params["include_managers"]?.Value<bool>() ?? true;
                var maxManagers = @params["max_managers"]?.Value<int>() ?? 20;

                // Create options
                var options = new SnapshotOptions
                {
                    MaxManagerClasses = maxManagers,
                    Patterns = new SnapshotPatterns
                    {
                        EntryPointPatterns = new Dictionary<string, string>
                        {
                            { "SceneLoader", "Scene loading/management" },
                            { "LevelManager", "Level management" },
                            { "GameManager", "Game state management" },
                            { "GameController", "Main game controller" },
                            { "Bootstrap", "Initialization entry point" },
                            { "Main", "Main entry point" },
                            { "EntryPoint", "Explicit entry point" },
                            { "ApplicationInitializer", "App initialization" }
                        },
                        ManagerClassPatterns = new[] { "Manager", "Controller", "Service", "System", "Handler" },
                        ManagerExcludePrefixes = new[] { "Unity", "Editor", "Test" }
                    }
                };

                // Detect architecture type
                var archType = ArchitectureAnalyzer.DetectArchitectureType();

                // Detect loading strategy
                var loadingStrategy = ArchitectureAnalyzer.DetectLoadingStrategy();

                var result = new Dictionary<string, object>
                {
                    ["architecture_type"] = archType.Type,
                    ["architecture_confidence"] = archType.Confidence ?? "Unknown",
                    ["loading_strategy"] = loadingStrategy.Method,
                    ["loading_indicators"] = loadingStrategy.Indicators.ToArray()
                };

                // Get entry points if requested
                if (includeEntryPoints)
                {
                    var entryPoints = ArchitectureAnalyzer.FindEntryPoints(options);
                    result["entry_points"] = entryPoints.ConvertAll(ep => new
                    {
                        path = ep.Path,
                        reason = ep.Reason
                    }).ToArray();
                }

                // Get manager classes if requested
                if (includeManagers)
                {
                    var managers = ArchitectureAnalyzer.FindManagerClasses(options);
                    result["manager_classes"] = managers.ToArray();
                    result["manager_count"] = managers.Count;
                }

                return new SuccessResponse("Architecture analysis complete.", result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error analyzing architecture: {e.Message}");
            }
        }
    }

    #endregion

    #region GetProjectDirectoryTool

    /// <summary>
    /// Returns project directory structure as a tree.
    /// Use when you need to explore the project folder structure without generating a full snapshot.
    /// </summary>
    [McpForUnityTool("get_project_directory", Description = "Returns project directory structure as a tree. Use when you need to explore the project folder structure without generating a full snapshot.")]
    public static class GetProjectDirectoryTool
    {
        /// <summary>
        /// Parameters for get_project_directory tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Root path to start from (default: Assets).
            /// </summary>
            [ToolParameter("Root path to start from (default: Assets)", Required = false, DefaultValue = "Assets")]
            public string RootPath { get; set; } = "Assets";

            /// <summary>
            /// Maximum depth to traverse.
            /// </summary>
            [ToolParameter("Maximum depth to traverse", Required = false, DefaultValue = "4")]
            public int MaxDepth { get; set; } = 4;

            /// <summary>
            /// Whether to include file counts.
            /// </summary>
            [ToolParameter("Whether to include file counts", Required = false, DefaultValue = "true")]
            public bool IncludeFileCounts { get; set; } = true;

            /// <summary>
            /// Whether to include Packages folder.
            /// </summary>
            [ToolParameter("Whether to include Packages folder", Required = false, DefaultValue = "false")]
            public bool IncludePackages { get; set; } = false;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var rootPath = @params["root_path"]?.ToString() ?? "Assets";
                var maxDepth = @params["max_depth"]?.Value<int>() ?? 4;
                var includeFileCounts = @params["include_file_counts"]?.Value<bool>() ?? true;
                var includePackages = @params["include_packages"]?.Value<bool>() ?? false;

                var options = new SnapshotOptions
                {
                    MaxDepth = maxDepth,
                    IncludePackages = includePackages
                };

                var tree = QueryHelpers.GenerateDirectoryTree(rootPath, 0, maxDepth, options, includeFileCounts);
                var stats = QueryHelpers.CalculateDirectoryStats(rootPath, includePackages);

                return new SuccessResponse("Directory structure generated.", new
                {
                    root_path = rootPath,
                    max_depth = maxDepth,
                    directory_tree = tree,
                    stats = stats
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting directory structure: {e.Message}");
            }
        }
    }

    #endregion

    #region GetDataSchemaTool

    /// <summary>
    /// Returns data schema information including ScriptableObject definitions and JSON data examples.
    /// Use when you need to understand the data structures in the project.
    /// </summary>
    [McpForUnityTool("get_data_schema", Description = "Returns data schema information including ScriptableObject definitions and JSON data examples. Use when you need to understand the data structures in the project.")]
    public static class GetDataSchemaTool
    {
        /// <summary>
        /// Parameters for get_data_schema tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Whether to include ScriptableObject definitions.
            /// </summary>
            [ToolParameter("Whether to include ScriptableObject definitions", Required = false, DefaultValue = "true")]
            public bool IncludeScriptableObjects { get; set; } = true;

            /// <summary>
            /// Whether to include JSON data examples.
            /// </summary>
            [ToolParameter("Whether to include JSON data examples", Required = false, DefaultValue = "true")]
            public bool IncludeJsonExamples { get; set; } = true;

            /// <summary>
            /// Maximum number of ScriptableObjects to return.
            /// </summary>
            [ToolParameter("Maximum number of ScriptableObjects to return", Required = false, DefaultValue = "10")]
            public int MaxScriptableObjects { get; set; } = 10;

            /// <summary>
            /// Maximum number of JSON examples to return.
            /// </summary>
            [ToolParameter("Maximum number of JSON examples to return", Required = false, DefaultValue = "3")]
            public int MaxJsonExamples { get; set; } = 3;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var includeScriptableObjects = @params["include_scriptable_objects"]?.Value<bool>() ?? true;
                var includeJsonExamples = @params["include_json_examples"]?.Value<bool>() ?? true;
                var maxScriptableObjects = @params["max_scriptable_objects"]?.Value<int>() ?? 10;
                var maxJsonExamples = @params["max_json_examples"]?.Value<int>() ?? 3;

                var options = new SnapshotOptions
                {
                    MaxScriptableObjects = maxScriptableObjects,
                    MaxJsonExamples = maxJsonExamples,
                    MaxFilesToScan = 30
                };

                var result = new Dictionary<string, object>();

                // Get ScriptableObject types if requested
                if (includeScriptableObjects)
                {
                    var scriptableObjects = DataSchemaAnalyzer.FindScriptableObjectDataTypes(options);
                    result["scriptable_objects"] = scriptableObjects.ConvertAll(so => new
                    {
                        path = so.Path,
                        class_name = so.ClassName,
                        code_snippet = so.CodeSnippet
                    }).ToArray();
                    result["scriptable_object_count"] = scriptableObjects.Count;
                }

                // Get JSON examples if requested
                if (includeJsonExamples)
                {
                    var jsonFiles = DataSchemaAnalyzer.FindJsonDataFiles(options);
                    result["json_examples"] = jsonFiles.ConvertAll(jf => new
                    {
                        path = jf.Path,
                        content = jf.Content
                    }).ToArray();
                    result["json_example_count"] = jsonFiles.Count;
                }

                return new SuccessResponse("Data schema retrieved.", result);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting data schema: {e.Message}");
            }
        }
    }

    #endregion

}
