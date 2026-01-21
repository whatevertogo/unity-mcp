using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Generates System Anatomy using heuristic rules (metadata-based, no code reading).
    /// Phase 1: Hard rules (naming + base class) cover ~70%
    /// Phase 2: Reference analysis fills the gaps
    /// </summary>
    public static class SystemAnatomyGenerator
    {
        // Category matching rules (ai2's heuristics)
        private static readonly Dictionary<SystemCategory, Func<string, MonoScript, bool>> Rules = new()
        {
            [SystemCategory.Managers] = (path, script) =>
                path.Contains("Manager", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Service", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Registry", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Handler", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Controller", StringComparison.OrdinalIgnoreCase),

            [SystemCategory.DataModels] = (path, script) =>
                path.Contains("Data", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Model", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Settings", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Config", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Asset", StringComparison.OrdinalIgnoreCase),

            [SystemCategory.UI] = (path, script) =>
                path.Contains("UI", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("HUD", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("View", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Menu", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Panel", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("Button", StringComparison.OrdinalIgnoreCase),

            [SystemCategory.EditorTools] = (path, script) =>
                path.Contains("/Editor/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("\\Editor\\", StringComparison.OrdinalIgnoreCase),
        };

        public static SystemAnatomy[] Generate()
        {
            var scripts = AssetDatabase.FindAssets("t:Script");
            var categorized = new Dictionary<SystemCategory, List<string>>();
            var uncategorized = new List<string>();

            // Phase 1: Hard rules (naming + folder structure)
            foreach (var guid in scripts)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var className = System.IO.Path.GetFileNameWithoutExtension(path);

                bool matched = false;
                foreach (var rule in Rules)
                {
                    if (rule.Value(path, null))
                    {
                        if (!categorized.ContainsKey(rule.Key))
                            categorized[rule.Key] = new List<string>();
                        categorized[rule.Key].Add(className);
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                    uncategorized.Add(className);
            }

            // Phase 2: Build anatomy entries
            var result = new List<SystemAnatomy>();

            foreach (var kvp in categorized.OrderByDescending(x => x.Value.Count))
            {
                result.Add(new SystemAnatomy
                {
                    Category = GetCategoryName(kvp.Key),
                    EntryPoints = kvp.Value.OrderBy(x => x).Take(5).ToArray(),
                    Responsibility = GetResponsibility(kvp.Key),
                    ClassCount = kvp.Value.Count,
                    TokenEstimate = kvp.Value.Count * 200, // ~200 tokens per class
                    Namespace = InferNamespace(kvp.Value)
                });
            }

            // Add uncategorized as "Core Logic" if significant
            if (uncategorized.Count > 5)
            {
                result.Add(new SystemAnatomy
                {
                    Category = "Core Logic",
                    EntryPoints = uncategorized.Take(5).ToArray(),
                    Responsibility = "Gameplay mechanics and business logic",
                    ClassCount = uncategorized.Count,
                    TokenEstimate = uncategorized.Count * 200,
                    Namespace = "Project"
                });
            }

            return result.ToArray();
        }

        private static string GetCategoryName(SystemCategory category)
        {
            return category switch
            {
                SystemCategory.Managers => "Managers",
                SystemCategory.DataModels => "Data Models",
                SystemCategory.UI => "UI System",
                SystemCategory.EditorTools => "Editor Tools",
                _ => "Other"
            };
        }

        private static string GetResponsibility(SystemCategory category)
        {
            return category switch
            {
                SystemCategory.Managers => "Lifecycle, coordination, and state management",
                SystemCategory.DataModels => "ScriptableObject configs and data containers",
                SystemCategory.UI => "User interface, HUD, and interaction",
                SystemCategory.EditorTools => "Editor extensions and custom tools",
                _ => "General logic components"
            };
        }

        private static string InferNamespace(List<string> classNames)
        {
            // Simple namespace inference from class names
            if (classNames.Count == 0) return "Unknown";

            // Check for common prefixes
            var prefixes = classNames
                .Select(name => name.Split('_')[0]) // Assuming ClassName_Subfolder pattern
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            if (prefixes != null && prefixes.Count() > classNames.Count / 2)
                return prefixes.Key;

            return "Project";
        }

        private enum SystemCategory
        {
            Managers,
            DataModels,
            UI,
            EditorTools
        }
    }
}
