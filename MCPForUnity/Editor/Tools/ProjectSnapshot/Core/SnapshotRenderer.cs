using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Renders snapshot data into AI-optimized Markdown templates.
    /// This is the "Presentation Layer" of the three-tier architecture.
    /// </summary>
    public static class SnapshotRenderer
    {
        /// <summary>
        /// Renders the project snapshot in "Golden Template" format.
        /// Called by get_project_snapshot tool.
        /// </summary>
        public static string RenderSnapshot(
            SnapshotMetadata metadata,
            ProjectIdentity identity,
            SystemAnatomy[] systems,
            MentalMap mentalMap)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# 🛠 Project Architecture Snapshot");
            sb.AppendLine($"*Timestamp: {metadata.GeneratedAt} | Unity: {metadata.UnityVersion} | Pipeline: {metadata.RenderPipeline}*");
            sb.AppendLine();

            // Strategic Overview
            sb.AppendLine("## 🎯 Strategic Overview");
            sb.AppendLine($"- **Project Identity**: {identity.Type}");
            sb.AppendLine($"- **Core Loop/Logic**: {identity.CoreLoop}");
            sb.AppendLine($"- **Scale**: {identity.Scale} ({metadata.ScriptCount} scripts, {metadata.SceneCount} scenes)");
            sb.AppendLine($"- **Detection**: {identity.DetectionMethod} ({identity.Confidence}% confidence)");
            sb.AppendLine();

            // System Anatomy (semantic table)
            sb.AppendLine("## 🏗 System Anatomy (High Level)");
            sb.AppendLine("| Category | Key Classes / Entry Points | Responsibility |");
            sb.AppendLine("| :--- | :--- | :--- |");

            foreach (var system in systems.OrderByDescending(s => s.ClassCount))
            {
                var entries = system.EntryPoints.Length > 0
                    ? string.Join(", ", system.EntryPoints.Take(3).Select(e => $"`{e}`"))
                    : "(auto-detected)";
                sb.AppendLine($"| **{system.Category}** | {entries} | {system.Responsibility} |");
            }
            sb.AppendLine();

            // Mental Map
            sb.AppendLine("## 📂 Structural Mental Map");

            // Namespaces
            if (mentalMap.Namespaces != null && mentalMap.Namespaces.Length > 0)
            {
                sb.AppendLine("**Namespaces**:");
                foreach (var ns in mentalMap.Namespaces.OrderByDescending(n => n.Percentage))
                {
                    sb.AppendLine($"- `{ns.Namespace}`: {ns.ClassCount} classes ({ns.Percentage}%) - {ns.Purpose}");
                }
                sb.AppendLine();
            }

            // Folder Heatmap
            if (mentalMap.Hotspots != null && mentalMap.Hotspots.Length > 0)
            {
                sb.AppendLine("**Folder Heatmap** (Top 2 Levels):");
                foreach (var spot in mentalMap.Hotspots.Take(5))
                {
                    var activity = spot.Activity switch
                    {
                        ActivityLevel.High => "🔥 High",
                        ActivityLevel.Medium => "🧊 Medium",
                        ActivityLevel.Low => "❄️ Low",
                        ActivityLevel.Dormant => "💀 Dormant",
                        _ => "?"
                    };
                    sb.AppendLine($"- 📁 `{spot.Path}` [{spot.FileCount} files, {activity} Activity]");
                }
                sb.AppendLine();
            }

            // AI Protocol (Critical!)
            sb.AppendLine("> [!IMPORTANT]");
            sb.AppendLine("> **AI Protocol**:");
            sb.AppendLine("> 1. Before modifying logic, check `Assets/Settings` for ScriptableObject configs.");
            sb.AppendLine("> 2. For dependency analysis, use `inspect_dependency(focus_path=\"...\")`.");
            sb.AppendLine("> 3. Don't traverse `Assets/Tests` unless writing tests.");
            sb.AppendLine("> 4. New scripts should follow namespace conventions shown above.");

            return sb.ToString();
        }

        /// <summary>
        /// Renders the dependency heatmap in "Golden Template" format.
        /// Called by inspect_dependency tool.
        /// </summary>
        public static string RenderDependency(
            string focusPath,
            HotAssetInfo[] hotAssets,
            CircularDependencyChain[] circularDeps,
            int totalAssets)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# 🔗 Asset Intelligence Report");
            var context = string.IsNullOrEmpty(focusPath)
                ? "*Context: Global Analysis*"
                : $"*Context: Focus `{focusPath}`*";
            sb.AppendLine(context);
            sb.AppendLine();

            // Critical Alerts
            bool hasAlerts = (circularDeps != null && circularDeps.Length > 0);
            if (hasAlerts)
            {
                sb.AppendLine("## ⚠️ Critical Alerts");
                sb.AppendLine($"- **Circular Dependencies**: {circularDeps.Length} chain(s) detected!");
                foreach (var cycle in circularDeps.Take(3))
                {
                    var chain = string.Join(" → ", cycle.Path);
                    sb.AppendLine($"  - `{chain}` ({cycle.Severity})");
                    if (!string.IsNullOrEmpty(cycle.Recommendation))
                        sb.AppendLine($"    💡 _{cycle.Recommendation}_");
                }
                sb.AppendLine();
            }

            // Dependency Heatmap
            sb.AppendLine("## 📊 Dependency Heatmap");
            sb.AppendLine("| Asset Path | Ref Count | Type | Impact Level |");
            sb.AppendLine("| :--- | :--- | :--- | :--- |");

            var assetsToShow = (hotAssets ?? Array.Empty<HotAssetInfo>())
                .OrderByDescending(a => a.WeightedScore)
                .Take(10);

            foreach (var asset in assetsToShow)
            {
                var impact = GetImpactIcon(asset.Impact);
                var risk = asset.RiskContext != null && asset.RiskContext.Length > 0
                    ? $" *{string.Join(", ", asset.RiskContext)}*"
                    : "";
                sb.AppendLine($"| `{asset.Path}` | {asset.ReferenceCount} | {asset.Type} | {impact}{risk} |");
            }
            sb.AppendLine();

            // Summary
            sb.AppendLine($"**Total Assets in Index**: {totalAssets}");

            // Refactoring Tip
            sb.AppendLine();
            sb.AppendLine("> [!TIP]");
            sb.AppendLine("> **Refactoring Insight**:");
            sb.AppendLine("> - 🔴 Critical assets: Modify via configuration, not code.");
            sb.AppendLine("> - 🟠 High assets: Expect cascading changes.");
            sb.AppendLine("> - 🟡 Medium assets: Local scope impact.");
            sb.AppendLine("> - 🟢 Low assets: Safe to refactor.");

            return sb.ToString();
        }

        /// <summary>
        /// Renders a focused dependency view for a specific asset.
        /// </summary>
        public static string RenderFocusedAsset(
            AssetSummary focus,
            List<AssetSummary> dependencies,
            List<AssetSummary> dependents)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"# 🔍 Asset Focus: `{focus.Name}`");
            sb.AppendLine($"*Type: {focus.Type} | References: {focus.ReferenceCount} | Impact: {GetImpactName(focus)}*");
            sb.AppendLine();

            // Dependencies (what this asset depends on)
            if (dependencies != null && dependencies.Count > 0)
            {
                sb.AppendLine("## 📥 Dependencies (what this needs)");
                sb.AppendLine("| Asset | Type | Impact |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var dep in dependencies.Take(10))
                {
                    sb.AppendLine($"| `{dep.Name}` | {dep.Type} | {GetImpactIcon(dep)} |");
                }
                sb.AppendLine();
            }

            // Dependents (what depends on this)
            if (dependents != null && dependents.Count > 0)
            {
                sb.AppendLine("## 📤 Dependents (what uses this)");
                sb.AppendLine("| Asset | Type | Impact |");
                sb.AppendLine("| :--- | :--- | :--- |");
                foreach (var dep in dependents.Take(10))
                {
                    sb.AppendLine($"| `{dep.Name}` | {dep.Type} | {GetImpactIcon(dep)} |");
                }
                sb.AppendLine();
            }

            // Risk warning if circular
            if (focus.IsCircular)
            {
                sb.AppendLine("> [!WARNING]");
                sb.AppendLine("> **Circular Reference Detected**: This asset is part of a circular dependency.");
                sb.AppendLine("> Exercise extreme caution when refactoring.");
            }

            return sb.ToString();
        }

        private static string GetImpactIcon(AssetSummary asset)
        {
            var impact = CalculateImpactLevel(asset.ReferenceCount);
            return GetImpactIcon(impact);
        }

        private static string GetImpactIcon(ImpactLevel level)
        {
            return level switch
            {
                ImpactLevel.Critical => "🔴 Critical",
                ImpactLevel.High => "🟠 High",
                ImpactLevel.Medium => "🟡 Medium",
                ImpactLevel.Low => "🟢 Low",
                _ => "⚪ Unknown"
            };
        }

        private static string GetImpactName(AssetSummary asset)
        {
            return CalculateImpactLevel(asset.ReferenceCount) switch
            {
                ImpactLevel.Critical => "Critical",
                ImpactLevel.High => "High",
                ImpactLevel.Medium => "Medium",
                ImpactLevel.Low => "Low",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Calculates impact level from reference count.
        /// Uses relative thresholds: Critical > 20 or Top 3%, High = 10-20 or Top 10%.
        /// </summary>
        public static ImpactLevel CalculateImpactLevel(int referenceCount, int totalAssets = 100)
        {
            // Absolute thresholds
            if (referenceCount > 20) return ImpactLevel.Critical;
            if (referenceCount >= 10) return ImpactLevel.High;
            if (referenceCount >= 3) return ImpactLevel.Medium;
            if (referenceCount >= 1) return ImpactLevel.Low;

            return ImpactLevel.Low;
        }

        /// <summary>
        /// Calculates impact level with relative percentile.
        /// </summary>
        public static ImpactLevel CalculateImpactLevel(int referenceCount, int totalAssets, float percentile)
        {
            // Combine absolute and relative thresholds
            if (referenceCount > 20 || percentile < 0.03f) return ImpactLevel.Critical;
            if (referenceCount >= 10 || percentile < 0.10f) return ImpactLevel.High;
            if (referenceCount >= 3) return ImpactLevel.Medium;
            return ImpactLevel.Low;
        }

        /// <summary>
        /// Generates risk context strings for an asset.
        /// </summary>
        public static string[] GenerateRiskContext(AssetSummary asset)
        {
            var risks = new List<string>();

            if (asset.ReferenceCount > 20)
                risks.Add("Global config");

            if (asset.Path.Contains("Manager") || asset.Path.Contains("System"))
                risks.Add("Core system");

            if (asset.SemanticLinks != null && asset.SemanticLinks.Count > 0)
                risks.Add($"Connected via {asset.SemanticLinks[0]}");

            return risks.ToArray();
        }
    }
}
