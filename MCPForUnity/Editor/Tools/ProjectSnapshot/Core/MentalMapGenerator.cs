using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Generates Mental Map using "Reference Density" algorithm (ai2's suggestion).
    /// Shows Namespace distribution as primary, Folder heatmap as secondary.
    /// </summary>
    public static class MentalMapGenerator
    {
        public static MentalMap Generate()
        {
            return new MentalMap
            {
                Namespaces = AnalyzeNamespaces(),
                Hotspots = AnalyzeFolderHeatmap().Take(5).ToArray(),
                RecommendedLocation = InferRecommendedLocation()
            };
        }

        /// <summary>
        /// Analyzes namespace distribution.
        /// Format: "Project.Core: 60%" - [Core Battle Logic]
        /// </summary>
        private static NamespaceDistribution[] AnalyzeNamespaces()
        {
            var scripts = AssetDatabase.FindAssets("t:Script");
            var namespaceGroups = new Dictionary<string, List<string>>();

            foreach (var guid in scripts)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ns = InferNamespaceFromPath(path);
                var className = System.IO.Path.GetFileNameWithoutExtension(path);

                if (!namespaceGroups.ContainsKey(ns))
                    namespaceGroups[ns] = new List<string>();
                namespaceGroups[ns].Add(className);
            }

            var total = namespaceGroups.Values.Sum(x => x.Count);
            var result = new List<NamespaceDistribution>();

            foreach (var kvp in namespaceGroups.OrderByDescending(x => x.Value.Count))
            {
                result.Add(new NamespaceDistribution
                {
                    Namespace = kvp.Key,
                    ClassCount = kvp.Value.Count,
                    Percentage = (int)((float)kvp.Value.Count / total * 100),
                    Purpose = InferPurpose(kvp.Key)
                });
            }

            return result.ToArray();
        }

        /// <summary>
        /// Analyzes folder heatmap using "Reference Density" algorithm.
        /// Heat = (file_count * 0.3) + (high_impact_count * 0.7)
        /// Only shows top 5 hottest folders.
        /// </summary>
        private static List<FolderHeatmapEntry> AnalyzeFolderHeatmap()
        {
            var folderStats = new Dictionary<string, FolderStats>();

            // Scan top-level folders under Assets/
            var folders = new[] { "Scripts", "Editor", "Tests", "Prefabs", "Scenes", "Settings", "Resources", "Art" };
            var assetsPath = Application.dataPath;

            foreach (var folder in folders)
            {
                var folderPath = $"Assets/{folder}";
                var guids = AssetDatabase.FindAssets("", new[] { folderPath });

                var fileCount = 0;
                var highImpactCount = 0;

                foreach (var guid in guids.Take(100)) // Sample for performance
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;

                    fileCount++;

                    // Check if high impact (simplified heuristic)
                    if (IsHighImpactPath(path))
                        highImpactCount++;
                }

                folderStats[folder] = new FolderStats
                {
                    FileCount = fileCount,
                    HighImpactCount = highImpactCount
                };
            }

            // Calculate heat and sort
            var result = new List<FolderHeatmapEntry>();

            foreach (var kvp in folderStats.OrderByDescending(x => CalculateHeat(x.Value)))
            {
                var heat = CalculateHeat(kvp.Value);
                result.Add(new FolderHeatmapEntry
                {
                    Path = $"Assets/{kvp.Key}",
                    FileCount = kvp.Value.FileCount,
                    Activity = GetActivityLevel(heat),
                    HighImpactCount = kvp.Value.HighImpactCount
                });
            }

            return result;
        }

        /// <summary>
        /// Heat = (file_count * 0.3) + (high_impact_count * 0.7)
        /// </summary>
        private static float CalculateHeat(FolderStats stats)
        {
            return (stats.FileCount * 0.3f) + (stats.HighImpactCount * 0.7f);
        }

        private static ActivityLevel GetActivityLevel(float heat)
        {
            return heat switch
            {
                > 20 => ActivityLevel.High,
                > 10 => ActivityLevel.Medium,
                > 5 => ActivityLevel.Low,
                _ => ActivityLevel.Dormant
            };
        }

        private static bool IsHighImpactPath(string path)
        {
            // Simplified high-impact indicators
            return path.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        private static string InferNamespaceFromPath(string path)
        {
            // Extract namespace from folder structure
            var parts = path.Split('/', '\\');

            // Skip "Assets" and convert rest to namespace format
            var nsParts = new List<string>();
            foreach (var part in parts)
            {
                if (part == "Assets") continue;
                if (part.Contains(".")) continue; // Skip file extension
                nsParts.Add(part);
            }

            if (nsParts.Count == 0) return "Project";

            // Limit to 2 levels for readability
            return string.Join(".", nsParts.Take(2));
        }

        private static string InferPurpose(string ns)
        {
            return ns switch
            {
                var n when n.Contains("Editor") => "Editor tools and extensions",
                var n when n.Contains("Core") || n.Contains("Game") => "Core game logic",
                var n when n.Contains("UI") => "User interface components",
                var n when n.Contains("Data") || n.Contains("Model") => "Data structures and configs",
                var n when n.Contains("Util") || n.Contains("Helper") => "Utility functions",
                _ => "General code"
            };
        }

        private static string InferRecommendedLocation()
        {
            // Check for existing namespace patterns
            var scripts = AssetDatabase.FindAssets("t:Script");
            var nsCounts = new Dictionary<string, int>();

            foreach (var guid in scripts.Take(50))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ns = InferNamespaceFromPath(path);
                nsCounts[ns] = nsCounts.GetValueOrDefault(ns, 0) + 1;
            }

            // Recommend the most common namespace for new code
            var topNs = nsCounts.OrderByDescending(x => x.Value).FirstOrDefault();
            if (topNs.Key != null)
            {
                var folder = topNs.Key.Replace(".", "/");
                return $"Place new scripts under `Assets/{folder}` following namespace `{topNs.Key}`";
            }

            return "Place new scripts under `Assets/Scripts` with namespace `Project`";
        }

        private class FolderStats
        {
            public int FileCount;
            public int HighImpactCount;
        }
    }
}
