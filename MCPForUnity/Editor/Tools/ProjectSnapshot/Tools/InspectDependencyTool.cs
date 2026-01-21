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

    /// <summary>
    /// Inspects asset dependencies and project-level dependency patterns.
    /// Supports both focused analysis (specific asset) and global mode (hot assets & circular dependencies).
    /// Essential for refactoring and impact analysis.
    /// </summary>
    [McpForUnityTool("inspect_dependency",
        Description = "Inspect asset dependencies. Use focus_path for specific asset, or null for global hot assets & circular dependency warnings.")]
    public static class InspectDependencyTool
    {
        /// <summary>
        /// Parameters for inspect_dependency tool.
        /// </summary>
        public class Parameters
        {
            /// <summary>
            /// The center asset to query (e.g., 'Assets/Prefabs/MyPrefab.prefab').
            /// If null or empty, returns global statistics (hot assets + circular dependencies).
            /// </summary>
            [ToolParameter("Asset path to analyze (e.g., Assets/Prefabs/MyPrefab.prefab). If empty, returns global stats.",
                Required = false, DefaultValue = "")]
            public string FocusPath { get; set; }

            /// <summary>
            /// Depth of dependency graph (1=direct only, 2=with indirect).
            /// </summary>
            [ToolParameter("Depth of dependency graph (1=direct only, 2=with indirect). Default: 1",
                Required = false, DefaultValue = "1")]
            public int MaxDepth { get; set; } = 1;

            /// <summary>
            /// Include code snippets for scripts (lazy-extracted).
            /// </summary>
            [ToolParameter("Include code snippets for scripts. Default: false",
                Required = false, DefaultValue = "false")]
            public bool IncludeCode { get; set; } = false;

            /// <summary>
            /// Query strategy: balanced (filter raw resources), deep (include all), slim (paths only).
            /// </summary>
            [ToolParameter("Query strategy: balanced/deep/slim. Default: balanced",
                Required = false, DefaultValue = "balanced")]
            public string Strategy { get; set; } = "balanced";

            /// <summary>
            /// Include reverse dependents (assets that depend on this).
            /// </summary>
            [ToolParameter("Include reverse dependents. Default: true",
                Required = false, DefaultValue = "true")]
            public bool IncludeDependents { get; set; } = true;

            /// <summary>
            /// Auto-generate index if missing.
            /// </summary>
            [ToolParameter("Auto-generate index if missing. Default: true",
                Required = false, DefaultValue = "true")]
            public bool AutoGenerateIndex { get; set; } = true;

            /// <summary>
            /// For global mode: maximum number of hot assets to return.
            /// </summary>
            [ToolParameter("Max hot assets to return in global mode. Default: 10",
                Required = false, DefaultValue = "10")]
            public int TopN { get; set; } = 10;
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                var focusPath = @params["focus_path"]?.ToString() ?? @params["asset_path"]?.ToString();

                // Global mode: return hot assets and circular dependencies
                if (string.IsNullOrEmpty(focusPath))
                {
                    return HandleGlobalMode(@params);
                }

                // Focus mode: analyze specific asset
                return HandleFocusMode(@params, focusPath);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error inspecting dependencies: {e.Message}");
            }
        }

        /// <summary>
        /// Handles global mode - returns hot assets and circular dependencies using Golden Template format.
        /// </summary>
        private static object HandleGlobalMode(JObject @params)
        {
            var topN = @params["top_n"]?.Value<int>() ?? 10;
            var autoGenerateIndex = @params["auto_generate_index"]?.Value<bool>() ?? true;

            if (!QueryHelpers.EnsureIndexLoaded(autoGenerateIndex))
            {
                return new ErrorResponse("Dependency index not found. Set auto_generate_index=true first.");
            }

            var index = QueryHelpers.Index;
            if (!index.IsLoaded)
            {
                return new ErrorResponse("Index not loaded.");
            }

            // Get hot assets with impact level analysis
            var hotAssetsRaw = GetHotAssets(index, topN);
            var hotAssetInfos = ConvertToHotAssetInfos(hotAssetsRaw, index.Count);

            // Get circular dependencies
            var options = ProjectSnapshotSettings.Instance?.ToOptions() ?? new SnapshotOptions();
            var circularDepsRaw = AssetRegistryAnalyzer.FindCircularDependencies(options);
            var circularDepInfos = ConvertToCircularDependencyInfos(circularDepsRaw);

            // Render using Golden Template format
            var renderedContent = SnapshotRenderer.RenderDependency(
                "",  // No focus path for global mode
                hotAssetInfos,
                circularDepInfos,
                index.Count
            );

            return new SuccessResponse(
                $"Asset Intelligence Report: {index.Count} total assets, {hotAssetInfos.Length} hot assets shown." +
                (circularDepInfos.Length > 0 ? $" {circularDepInfos.Length} circular dependencies detected." : ""),
                new
                {
                    rendered_content = renderedContent,
                    total_assets = index.Count,
                    hot_assets = hotAssetInfos,
                    circular_dependencies = circularDepInfos.Length > 0 ? circularDepInfos : null,
                    next_actions = new[]
                    {
                        "Use inspect_dependency with focus_path to analyze specific assets in detail."
                    }
                }
            );
        }

        /// <summary>
        /// Converts raw hot assets to HotAssetInfo with impact levels.
        /// </summary>
        private static HotAssetInfo[] ConvertToHotAssetInfos(List<object> rawAssets, int totalAssets)
        {
            var result = new List<HotAssetInfo>();

            foreach (dynamic asset in rawAssets)
            {
                string path = asset.path;
                string type = asset.type;
                int refCount = asset.reference_count;

                var impact = SnapshotRenderer.CalculateImpactLevel(refCount, totalAssets);
                var risks = GenerateRiskContext(path, refCount, impact);

                result.Add(new HotAssetInfo
                {
                    Path = path,
                    ReferenceCount = refCount,
                    Type = type,
                    Impact = impact,
                    RiskContext = risks,
                    WeightedScore = CalculateWeightedScore(refCount, impact)
                });
            }

            return result.ToArray();
        }

        /// <summary>
        /// Converts raw circular dependencies to CircularDependencyChain.
        /// Input is List<List<string>> where each inner list is a circular chain.
        /// </summary>
        private static CircularDependencyChain[] ConvertToCircularDependencyInfos(List<List<string>> rawDeps)
        {
            var result = new List<CircularDependencyChain>();

            foreach (var chain in rawDeps)
            {
                var severity = chain.Count > 5 ? "High" : chain.Count > 3 ? "Medium" : "Low";
                var recommendation = "Consider refactoring to break this circular reference.";

                result.Add(new CircularDependencyChain
                {
                    Path = chain.ToArray(),
                    Severity = severity,
                    Recommendation = recommendation
                });
            }

            return result.ToArray();
        }

        /// <summary>
        /// Generates risk context for an asset.
        /// </summary>
        private static string[] GenerateRiskContext(string path, int refCount, ImpactLevel impact)
        {
            var risks = new List<string>();

            if (impact == ImpactLevel.Critical)
                risks.Add("Global config - modify via settings");

            if (path.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("System", StringComparison.OrdinalIgnoreCase))
                risks.Add("Core system - expect cascading changes");

            if (path.Contains("Editor", StringComparison.OrdinalIgnoreCase))
                risks.Add("Editor-only - safe for runtime");

            return risks.ToArray();
        }

        /// <summary>
        /// Calculates weighted score for sorting.
        /// </summary>
        private static float CalculateWeightedScore(int refCount, ImpactLevel impact)
        {
            float impactMultiplier = impact switch
            {
                ImpactLevel.Critical => 2.0f,
                ImpactLevel.High => 1.5f,
                ImpactLevel.Medium => 1.0f,
                ImpactLevel.Low => 0.5f,
                _ => 0.5f
            };
            return refCount * impactMultiplier;
        }

        /// <summary>
        /// Gets hot assets sorted by reference count.
        /// </summary>
        private static List<object> GetHotAssets(DependencyIndex index, int topN)
        {
            var hotAssets = new List<object>();

            // Check prefabs
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
                        weighted_score = result.TotalDependents * 1.0f
                    });
                }
            }

            // Check ScriptableObjects
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
                        weighted_score = result.TotalDependents * 1.0f
                    });
                }
            }

            return hotAssets
                .OrderByDescending(a => (float)((dynamic)a).weighted_score)
                .Take(topN)
                .ToList();
        }

        /// <summary>
        /// Handles focus mode - analyzes specific asset dependencies.
        /// </summary>
        private static object HandleFocusMode(JObject @params, string focusPath)
        {
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
                return new ErrorResponse("Dependency index not found. Set auto_generate_index=true first.");
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
}
