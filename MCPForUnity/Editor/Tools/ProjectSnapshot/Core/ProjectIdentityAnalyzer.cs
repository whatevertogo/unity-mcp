using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes project to determine its identity (Editor Tool / Game / VR, etc.)
    /// Uses hybrid weighted scoring based on dependencies, namespaces, and directory structure.
    /// </summary>
    public static class ProjectIdentityAnalyzer
    {
        /// <summary>
        /// Analyzes the project and determines its identity.
        /// </summary>
        public static ProjectIdentity Analyze()
        {
            var scores = new Dictionary<ProjectType, float>();

            // Dimension A: Dependency Analysis (weight: 0.5)
            var depScores = AnalyzeByDependencies();
            foreach (var kvp in depScores)
                scores[kvp.Key] = scores.GetValueOrDefault(kvp.Key, 0) + kvp.Value * 0.5f;

            // Dimension B: Code Statistics (weight: 0.3)
            var codeScores = AnalyzeByCodeStatistics();
            foreach (var kvp in codeScores)
                scores[kvp.Key] = scores.GetValueOrDefault(kvp.Key, 0) + kvp.Value * 0.3f;

            // Dimension C: Directory Structure (weight: 0.2)
            var dirScores = AnalyzeByDirectoryStructure();
            foreach (var kvp in dirScores)
                scores[kvp.Key] = scores.GetValueOrDefault(kvp.Key, 0) + kvp.Value * 0.2f;

            // Extract README for ground-truth description
            var readmePreview = ReadmeExtractor.ExtractPreview();

            // Find highest score
            var winner = scores.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
            var winnerType = winner.Key != default ? winner.Key : ProjectType.Unknown;
            var confidence = (int)(winner.Value * 100);

            return new ProjectIdentity
            {
                Type = GetDisplayName(winnerType),
                CoreLoop = InferCoreLoop(winnerType),
                Scale = InferScale(),
                DetectionMethod = "Hybrid Weighted Scoring",
                Confidence = confidence,
                Description = readmePreview
            };
        }

        /// <summary>
        /// Dimension A: Analyze by package dependencies.
        /// </summary>
        private static Dictionary<ProjectType, float> AnalyzeByDependencies()
        {
            var scores = new Dictionary<ProjectType, float>();

            // Check manifest.json for game-related packages
            var manifestPath = "Packages/manifest.json";
            if (System.IO.File.Exists(manifestPath))
            {
                var content = System.IO.File.ReadAllText(manifestPath);

                // Game indicators
                if (content.Contains("com.unity.inputsystem")) scores[ProjectType.Game] += 0.3f;
                if (content.Contains("com.unity.physics")) scores[ProjectType.Game] += 0.2f;
                if (content.Contains("com.unity.textmeshpro")) scores[ProjectType.Game] += 0.1f;

                // XR indicators
                if (content.Contains("com.unity.xr") || content.Contains("com.unity.xr.openxr"))
                    scores[ProjectType.XR] += 0.5f;

                // Editor tool indicators (negative for game)
                if (content.Contains("com.unity.editor") || content.Contains("com.unity.editorcoroutines"))
                    scores[ProjectType.EditorTool] += 0.3f;
            }

            // Check asmdef references
            var asmdefs = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (var guid in asmdefs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var content = System.IO.File.ReadAllText(path);

                // Editor-only asmdef
                if (content.Contains("UnityEditor") || path.Contains("/Editor/"))
                    scores[ProjectType.EditorTool] += 0.2f;
            }

            return scores;
        }

        /// <summary>
        /// Dimension B: Analyze by code statistics (namespace ratio).
        /// </summary>
        private static Dictionary<ProjectType, float> AnalyzeByCodeStatistics()
        {
            var scores = new Dictionary<ProjectType, float>();
            var scripts = AssetDatabase.FindAssets("t:Script");

            int editorScripts = 0;
            int runtimeScripts = 0;
            int uiScripts = 0;
            int combatScripts = 0;
            int totalScripts = scripts.Length;

            foreach (var guid in scripts)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                if (path.Contains("/Editor/") || path.Contains("\\Editor\\"))
                    editorScripts++;
                else
                    runtimeScripts++;

                if (path.Contains("UI") || path.Contains("Menu"))
                    uiScripts++;

                if (path.Contains("Combat") || path.Contains("Player") || path.Contains("Enemy"))
                    combatScripts++;
            }

            // Editor tool: > 40% editor scripts
            if (totalScripts > 0)
            {
                float editorRatio = (float)editorScripts / totalScripts;
                if (editorRatio > 0.4f)
                    scores[ProjectType.EditorTool] += 0.8f;
                else if (editorRatio > 0.2f)
                    scores[ProjectType.EditorTool] += 0.4f;
            }

            // Game with UI focus: > 20% UI scripts
            if (totalScripts > 0)
            {
                float uiRatio = (float)uiScripts / totalScripts;
                if (uiRatio > 0.2f)
                    scores[ProjectType.Game] += 0.3f;
            }

            // Combat game: > 15% combat scripts
            if (totalScripts > 0)
            {
                float combatRatio = (float)combatScripts / totalScripts;
                if (combatRatio > 0.15f)
                    scores[ProjectType.Game] += 0.4f;
            }

            return scores;
        }

        /// <summary>
        /// Dimension C: Analyze by directory structure.
        /// </summary>
        private static Dictionary<ProjectType, float> AnalyzeByDirectoryStructure()
        {
            var scores = new Dictionary<ProjectType, float>();

            // Check for game-specific folders
            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var folders = new HashSet<string>();

            foreach (var guid in guids.Take(500)) // Sample for performance
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    folders.Add(path);
            }

            // Analyze folder names
            foreach (var folder in folders)
            {
                var name = System.IO.Path.GetFileName(folder.TrimEnd('/'));

                switch (name.ToLower())
                {
                    case "scripts":
                    case "game":
                    case "gameplay":
                    case "combat":
                    case "player":
                        scores[ProjectType.Game] += 0.1f;
                        break;

                    case "editor":
                    case "tools":
                    case "utility":
                        scores[ProjectType.EditorTool] += 0.15f;
                        break;

                    case "xr":
                    case "vr":
                    case "ar":
                        scores[ProjectType.XR] += 0.3f;
                        break;
                }
            }

            return scores;
        }

        /// <summary>
        /// Infers the core loop description based on project type.
        /// </summary>
        private static string InferCoreLoop(ProjectType type)
        {
            return type switch
            {
                ProjectType.EditorTool => "Unity Editor extension with custom tools and inspectors",
                ProjectType.Game => "Unity game with MonoBehaviour-based gameplay loop",
                ProjectType.XR => "XR/VR experience with interaction system",
                ProjectType.MobileGame => "Mobile-optimized game with performance considerations",
                _ => "General Unity project"
            };
        }

        /// <summary>
        /// Infers project scale from script count.
        /// </summary>
        private static string InferScale()
        {
            var scriptCount = AssetDatabase.FindAssets("t:Script").Length;

            if (scriptCount < 20)
                return $"Small ({scriptCount} scripts)";
            if (scriptCount < 100)
                return $"Medium ({scriptCount} scripts)";
            return $"Large ({scriptCount} scripts)";
        }

        private static string GetDisplayName(ProjectType type)
        {
            return type switch
            {
                ProjectType.EditorTool => "Editor Tool",
                ProjectType.Game => "Game",
                ProjectType.XR => "VR/XR Experience",
                ProjectType.MobileGame => "Mobile Game",
                _ => "General Project"
            };
        }

        private enum ProjectType
        {
            Unknown,
            EditorTool,
            Game,
            XR,
            MobileGame
        }
    }
}
