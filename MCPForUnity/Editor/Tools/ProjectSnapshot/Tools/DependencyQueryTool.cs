using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    using MCPForUnity.Editor.Helpers;

    #region SearchAssetDependencyTool

    /// <summary>
    /// Searches for asset dependencies with contextual filtering for AI.
    /// Returns focused asset + direct dependencies + reverse dependents.
    /// Includes global statistics to prevent blind spots.
    /// </summary>
    [McpForUnityTool("search_asset_dependency",
        Description = "Searches asset dependencies with contextual filtering for AI. " +
                      "Use focus parameter for minimal token usage. " +
                      "Returns focused asset + direct dependencies + reverse dependents. " +
                      "Includes global statistics to prevent blind spots.")]
    public static class SearchAssetDependencyTool
    {
        /// <summary>
        /// Parameters for search_asset_dependency tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// The center asset to query around (e.g., 'Assets/Prefabs/MyPrefab.prefab').
            /// </summary>
            [ToolParameter("The center asset to query around (e.g., Assets/Prefabs/MyPrefab.prefab)", Required = true)]
            public string FocusPath { get; set; }

            /// <summary>
            /// Depth of dependency graph (1=direct only, 2=with indirect).
            /// </summary>
            [ToolParameter("Depth of dependency graph (1=direct only, 2=with indirect). Default: 1", Required = false, DefaultValue = "1")]
            public int MaxDepth { get; set; } = 1;

            /// <summary>
            /// Include code snippets for scripts (lazy-extracted).
            /// </summary>
            [ToolParameter("Include code snippets for scripts (lazy-extracted). Default: false", Required = false, DefaultValue = "false")]
            public bool IncludeCode { get; set; } = false;

            /// <summary>
            /// Query strategy: balanced (filter raw resources), deep (include all), slim (paths only).
            /// </summary>
            [ToolParameter("Query strategy: balanced (filter raw resources), deep (include all), slim (paths only). Default: balanced", Required = false, DefaultValue = "balanced")]
            public string Strategy { get; set; } = "balanced";

            /// <summary>
            /// Whether to include reverse dependents (assets that depend on this).
            /// </summary>
            [ToolParameter("Whether to include reverse dependents (assets that depend on this). Default: true", Required = false, DefaultValue = "true")]
            public bool IncludeDependents { get; set; } = true;

            /// <summary>
            /// Auto-generate index if missing.
            /// </summary>
            [ToolParameter("Auto-generate index if missing. Default: true", Required = false, DefaultValue = "true")]
            public bool AutoGenerateIndex { get; set; } = true;

            /// <summary>
            /// Legacy parameter: Path to the asset (maps to focus_path).
            /// </summary>
            [ToolParameter("Path to the asset (legacy, maps to focus_path)", Required = false, DefaultValue = "")]
            public string AssetPath { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                // Support both focus_path and legacy asset_path parameter
                var focusPath = @params["focus_path"]?.ToString() ?? @params["asset_path"]?.ToString();

                if (string.IsNullOrEmpty(focusPath))
                {
                    return new ErrorResponse("focus_path parameter is required.");
                }

                var maxDepth = @params["max_depth"]?.Value<int>() ?? 1;
                var includeCode = @params["include_code"]?.Value<bool>() ?? false;
                var strategyStr = @params["strategy"]?.ToString() ?? "balanced";
                var includeDependents = @params["include_dependents"]?.Value<bool>() ?? true;
                var autoGenerateIndex = @params["auto_generate_index"]?.Value<bool>() ?? true;

                // Parse strategy
                if (!Enum.TryParse<QueryStrategy>(strategyStr, true, out var strategy))
                {
                    strategy = QueryStrategy.Balanced;
                }

                // Ensure index is loaded
                if (!QueryHelpers.EnsureIndexLoaded(autoGenerateIndex))
                {
                    return new ErrorResponse("Dependency index not found. Set auto_generate_index=true or run regenerate_dependency_index first.");
                }

                // Use contextual query for AI-optimized results
                var result = QueryHelpers.Index.QueryContextual(
                    focusPath,
                    maxDepth,
                    includeCode,
                    strategy
                );

                if (result == null)
                {
                    // Asset not in index, try to get direct dependencies
                    var legacyResult = QueryHelpers.GetDirectDependencies(focusPath, includeDependents);
                    if (legacyResult != null)
                    {
                        return ConvertLegacyResult(legacyResult);
                    }
                    return new ErrorResponse($"Asset not found: {focusPath}");
                }

                // Check JSON size for auto-degradation
                var json = JsonConvert.SerializeObject(result, Formatting.None);
                if (json.Length > 10000)
                {
                    // Auto-degrade to slim view when data is too large
                    return new SuccessResponse(
                        $"Result too large ({json.Length} chars). Returning slim view.",
                        new
                        {
                            warning = "Data exceeded 10,000 chars. Showing slim view only.",
                            focus_path = result.FocusPath,
                            focus_name = result.FocusedAsset?.Name,
                            total_nodes = result.TotalNodesReturned,
                            global_stats = result.GlobalStats,
                            dependency_paths = result.Dependencies.Select(d => d.Path).ToList(),
                            dependent_paths = result.Dependents.Select(d => d.Path).ToList()
                        }
                    );
                }

                // Build the response without GUIDs (to save tokens)
                return new SuccessResponse(
                    $"Found {result.TotalNodesReturned} related assets for {result.FocusedAsset?.Name ?? focusPath}",
                    new
                    {
                        focus = result.FocusedAsset,
                        dependencies = result.Dependencies.Count > 0 ? result.Dependencies : null,
                        dependents = includeDependents && result.Dependents.Count > 0 ? result.Dependents : null,
                        global_stats = result.GlobalStats,
                        metadata = new
                        {
                            focus_path = result.FocusPath,
                            max_depth = result.MaxDepth,
                            total_nodes = result.TotalNodesReturned,
                            strategy = strategy.ToString().ToLower(),
                            timestamp = result.Timestamp.ToString("o")
                        }
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error searching dependencies: {e.Message}");
            }
        }

        /// <summary>
        /// Converts legacy DependencyQueryResult to the new format.
        /// </summary>
        private static object ConvertLegacyResult(DependencyQueryResult legacyResult)
        {
            return new SuccessResponse(
                $"Found {legacyResult.TotalDependencies} dependencies for {legacyResult.AssetPath}",
                new
                {
                    focus = new
                    {
                        path = legacyResult.AssetPath,
                        type = legacyResult.AssetType,
                        reference_count = legacyResult.TotalDependents
                    },
                    dependencies = legacyResult.Dependencies.Select(d => new
                    {
                        path = d.Path,
                        type = d.Type
                    }).ToArray(),
                    dependents = legacyResult.Dependents.Select(d => new
                    {
                        path = d.Path,
                        type = d.Type
                    }).ToArray(),
                    warning = "Asset not in index. Showing direct query results. Run regenerate_dependency_index for full context."
                }
            );
        }
    }

    #endregion

    #region GetIndexStatsTool

    /// <summary>
    /// Returns global index statistics for AI context awareness.
    /// Use this BEFORE deep queries to understand project scope.
    /// Identifies hot assets and potential refactoring risks.
    /// </summary>
    [McpForUnityTool("get_index_stats",
        Description = "Returns global index statistics for AI context awareness. " +
                      "Use this BEFORE deep queries to understand project scope. " +
                      "Identifies hot assets and potential refactoring risks.")]
    public static class GetIndexStatsTool
    {
        /// <summary>
        /// Parameters for get_index_stats tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Maximum number of hot assets to return.
            /// </summary>
            [ToolParameter("Maximum number of hot assets to return. Default: 10", Required = false, DefaultValue = "10")]
            public int TopN { get; set; } = 10;

            /// <summary>
            /// Filter by semantic level (1=scripts, 2=prefabs, 3=resources).
            /// </summary>
            [ToolParameter("Filter by semantic level (1=scripts, 2=prefabs, 3=resources). Default: all", Required = false, DefaultValue = "")]
            public string SemanticLevel { get; set; } = "";
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var topN = @params["top_n"]?.Value<int>() ?? 10;
                var semanticLevelStr = @params["semantic_level"]?.ToString();

                if (!QueryHelpers.EnsureIndexLoaded(autoGenerate: true))
                {
                    return new ErrorResponse("Index not available. Set auto_generate_index=true first.");
                }

                var index = QueryHelpers.Index;
                if (!index.IsLoaded)
                {
                    return new ErrorResponse("Index not loaded.");
                }

                // Calculate weighted hotness for all assets
                var hotAssets = new List<object>();

                foreach (var entry in index.GetAssetsByType("Prefab"))
                {
                    var path = entry;
                    var result = index.Query(path, includeDependents: true);
                    if (result != null)
                    {
                        hotAssets.Add(new
                        {
                            path = result.AssetPath,
                            type = result.AssetType,
                            reference_count = result.TotalDependents,
                            weighted_score = result.TotalDependents * 1.0f  // Prefab weight is 1.0
                        });
                    }
                }

                // Also add ScriptableObject stats if available
                foreach (var entry in index.GetAssetsByType("ScriptableObject"))
                {
                    var path = entry;
                    var result = index.Query(path, includeDependents: true);
                    if (result != null)
                    {
                        hotAssets.Add(new
                        {
                            path = result.AssetPath,
                            type = result.AssetType,
                            reference_count = result.TotalDependents,
                            weighted_score = result.TotalDependents * 1.0f  // ScriptableObject weight is 1.0
                        });
                    }
                }

                // Sort by weighted score and take top N
                var topHot = hotAssets
                    .OrderByDescending(a => (float)((dynamic)a).weighted_score)
                    .Take(topN)
                    .ToList();

                return new SuccessResponse(
                    $"Index contains {index.Count} total assets. Top {topHot.Count} hot assets shown.",
                    new
                    {
                        total_assets = index.Count,
                        top_hot_assets = topHot,
                        warning = "High reference_count indicates critical assets. Exercise caution when modifying."
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting index stats: {e.Message}");
            }
        }
    }

    #endregion

    #region SearchAssetsByNameTool

    /// <summary>
    /// Searches for assets by name pattern using the cached index.
    /// </summary>
    [McpForUnityTool("search_assets_by_name", Description = "Searches for assets by name pattern using the cached index.")]
    public static class SearchAssetsByNameTool
    {
        /// <summary>
        /// Parameters for search_assets_by_name tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Name pattern to search for (supports partial matching).
            /// </summary>
            [ToolParameter("Name pattern to search for (supports partial matching)", Required = true)]
            public string NamePattern { get; set; }

            /// <summary>
            /// Maximum number of results to return.
            /// </summary>
            [ToolParameter("Maximum number of results to return", Required = false, DefaultValue = "20")]
            public int MaxResults { get; set; } = 20;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var namePattern = @params["name_pattern"]?.ToString();
                if (string.IsNullOrEmpty(namePattern))
                {
                    return new ErrorResponse("name_pattern parameter is required.");
                }

                var maxResults = @params["max_results"]?.Value<int>() ?? 20;

                if (!QueryHelpers.EnsureIndexLoaded( autoGenerate: false))
                {
                    return new ErrorResponse("Dependency index not found. Run regenerate_dependency_index first.");
                }

                var results = QueryHelpers.Index.SearchByName(namePattern, maxResults);

                return new SuccessResponse($"Found {results.Count} assets matching '{namePattern}'", new
                {
                    name_pattern = namePattern,
                    total_found = results.Count,
                    assets = results.Select(e => new
                    {
                        path = e.AssetPath,
                        type = e.AssetType,
                        guid = e.AssetGuid
                    }).ToArray()
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error searching assets: {e.Message}");
            }
        }
    }

    #endregion

    #region GetAssetsByTypeTool

    /// <summary>
    /// Returns all assets of a specific type from the cached index.
    /// </summary>
    [McpForUnityTool("get_assets_by_type", Description = "Returns all assets of a specific type from the cached index.")]
    public static class GetAssetsByTypeTool
    {
        /// <summary>
        /// Parameters for get_assets_by_type tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// Asset type to filter (e.g., Prefab, Material, Texture, ScriptableObject).
            /// </summary>
            [ToolParameter("Asset type to filter (e.g., Prefab, Material, Texture, ScriptableObject)", Required = true)]
            public string AssetType { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var assetType = @params["asset_type"]?.ToString();
                if (string.IsNullOrEmpty(assetType))
                {
                    return new ErrorResponse("asset_type parameter is required (e.g., Prefab, Material, Texture).");
                }

                if (!QueryHelpers.EnsureIndexLoaded( autoGenerate: false))
                {
                    return new ErrorResponse("Dependency index not found. Run regenerate_dependency_index first.");
                }

                var assets = QueryHelpers.Index.GetAssetsByType(assetType);

                return new SuccessResponse($"Found {assets.Count} assets of type '{assetType}'", new
                {
                    asset_type = assetType,
                    total_found = assets.Count,
                    paths = assets.ToArray()
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting assets by type: {e.Message}");
            }
        }
    }

    #endregion
}
