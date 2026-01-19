using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// ScriptableObject-based settings for ProjectSnapshot.
    /// Automatically created at Assets/ProjectSnapshotSettings.asset when first accessed.
    /// Can also be created manually via: Assets > Create > MCP > ProjectSnapshot Settings
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectSnapshotSettings", menuName = "MCP/ProjectSnapshot Settings")]
    public sealed class ProjectSnapshotSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/ProjectSnapshotSettings.asset";

        private static ProjectSnapshotSettings _instance;

        [Header("Basic Options")]
        [Tooltip("Include Packages directory in snapshot")]
        public bool includePackages = false;

        [Tooltip("Maximum directory depth to scan (0 = unlimited)")]
        [Min(0)]
        public int maxDepth = 4;

        [Tooltip("Include dependency analysis for prefabs")]
        public bool includeDependencies = true;

        [Tooltip("Include data schema examples")]
        public bool includeDataSchemas = true;

        [Tooltip("Output file path (relative to Assets folder)")]
        public string outputPath = "Project_Snapshot.md";

        [Header("Cache Options")]
        [Tooltip("Enable snapshot caching")]
        public bool enableCache = true;

        [Tooltip("Cache validity duration in minutes (0 = unlimited)")]
        [Min(0)]
        public int cacheValidityMinutes = 60;

        [Tooltip("Auto-check for changes before regenerating")]
        public bool autoCheckDirty = true;

        [Header("Output Options")]
        [Tooltip("Separate dependencies into independent file")]
        public bool separateDependenciesFile = true;

        [Tooltip("Dependencies file output path")]
        public string dependenciesOutputPath = "Asset_Dependencies.md";

        [Tooltip("Generate dependency index for fast queries")]
        public bool generateDependencyIndex = true;

        [Header("Analysis Limits (0 = unlimited)")]
        [Min(0)]
        public int maxPrefabsToAnalyze = 50;

        [Min(0)]
        public int maxCorePrefabs = 20;

        [Min(0)]
        public int maxDependenciesPerPrefab = 10;

        [Min(0)]
        public int maxManagerClasses = 20;

        [Min(0)]
        public int maxScriptableObjects = 10;

        [Min(0)]
        public int maxFilesToScan = 30;

        [Min(0)]
        public int maxJsonExamples = 3;

        [Header("Smart Folding")]
        [Tooltip("Enable smart directory folding (resource-heavy folders collapse to summary)")]
        public bool enableSmartFolding = true;

        [Tooltip("File count threshold for triggering folder folding")]
        [Min(0)]
        public int foldingThreshold = 30;

        [Header("Circuit Breaker")]
        [Tooltip("Maximum token budget for main snapshot (approximate)")]
        [Min(1000)]
        public int maxSnapshotTokens = 5000;

        [Tooltip("Maximum token budget for dependencies file (approximate)")]
        [Min(1000)]
        public int maxDependencyTokens = 8000;

        [Tooltip("Maximum number of prefabs to include in snapshot")]
        [Min(0)]
        public int maxPrefabsInSnapshot = 200;

        [Header("Priority Settings")]
        [Tooltip("Core naming keywords for identifying important prefabs")]
        public string[] coreNamingKeywords = new string[]
        {
            "manager", "controller", "handler", "system",
            "core", "main", "game", "player", "ui"
        };

        [Tooltip("Number of top dependencies to show per prefab (tree view, recommended: 1-10)")]
        [Range(1, 10)]
        public int topDependenciesToShow = 3;

        [Header("Entry Point Patterns")]
        [Tooltip("Filename patterns to detect as entry points. Format: pattern|description")]
        public string[] entryPointPatterns = new string[]
        {
            "SceneLoader|Scene loading/management",
            "LevelManager|Level management",
            "GameManager|Game state management",
            "GameController|Main game controller",
            "Bootstrap|Initialization entry point",
            "Main|Main entry point",
            "EntryPoint|Explicit entry point",
            "ApplicationInitializer|App initialization"
        };

        [Header("Manager Class Patterns")]
        [Tooltip("Suffix patterns to detect manager classes")]
        public string[] managerClassPatterns = new string[]
        {
            "Manager", "Controller", "Service", "System", "Handler"
        };

        [Tooltip("Prefixes to exclude from manager class detection")]
        public string[] managerExcludePrefixes = new string[]
        {
            "Unity", "Editor", "Test"
        };

        #region Auto Generation Settings

        [Header("Auto Generation")]
        [Tooltip("Enable automatic snapshot generation")]
        public bool autoGenerateEnabled = true;

        [Tooltip("Generate snapshot automatically after script compilation")]
        public bool autoGenerateOnCompile = true;

        [Tooltip("Generate snapshot automatically after scene save")]
        public bool autoGenerateOnSceneSave = false;

        [Tooltip("Minimum interval between auto-generations (seconds)")]
        [Min(5)]
        public int autoGenerateMinIntervalSeconds = 60;

        [Tooltip("Only generate if project has changed (uses dirty check)")]
        public bool autoGenerateOnlyIfDirty = true;

        [Tooltip("Silent mode: no progress dialogs, only console logs")]
        public bool autoGenerateSilentMode = true;

        [Tooltip("Delay after editor becomes idle before generating (seconds)")]
        [Min(0)]
        public int autoGenerateIdleDelaySeconds = 3;

        #endregion

        [Header("Entry Point Exclude Paths")]
        [Tooltip("Path fragments to exclude from entry point detection (e.g., Tests, Editor, Plugins)")]
        public string[] entryPointExcludePaths = new string[]
        {
            "Tests", "Editor", "Plugins", "ThirdParty"
        };

        /// <summary>
        /// Converts this settings object to SnapshotOptions.
        /// </summary>
        public SnapshotOptions ToOptions()
        {
            return new SnapshotOptions
            {
                IncludePackages = includePackages,
                MaxDepth = maxDepth,
                IncludeDependencies = includeDependencies,
                IncludeDataSchemas = includeDataSchemas,
                OutputPath = outputPath,
                MaxPrefabsToAnalyze = maxPrefabsToAnalyze,
                MaxCorePrefabs = maxCorePrefabs,
                MaxDependenciesPerPrefab = maxDependenciesPerPrefab,
                MaxManagerClasses = maxManagerClasses,
                MaxScriptableObjects = maxScriptableObjects,
                MaxFilesToScan = maxFilesToScan,
                MaxJsonExamples = maxJsonExamples,
                UseCache = enableCache,
                CacheValidityMinutes = cacheValidityMinutes,
                SeparateDependenciesFile = separateDependenciesFile,
                DependenciesOutputPath = dependenciesOutputPath,
                GenerateIndex = generateDependencyIndex,
                // New fields for smart folding and circuit breaker
                EnableSmartFolding = enableSmartFolding,
                FoldingThreshold = foldingThreshold,
                MaxSnapshotTokens = maxSnapshotTokens,
                MaxDependencyTokens = maxDependencyTokens,
                MaxPrefabsInSnapshot = maxPrefabsInSnapshot,
                CoreNamingKeywords = coreNamingKeywords,
                TopDependenciesToShow = topDependenciesToShow,
                // Patterns
                Patterns = new SnapshotPatterns
                {
                    EntryPointPatterns = ParseEntryPointPatterns(),
                    EntryPointExcludePaths = entryPointExcludePaths,
                    ManagerClassPatterns = managerClassPatterns,
                    ManagerExcludePrefixes = managerExcludePrefixes
                }
            };
        }

        /// <summary>
        /// Enables all patterns with reasonable defaults.
        /// </summary>
        public void EnableDefaultPatterns()
        {
            entryPointPatterns = new string[]
            {
                "SceneLoader|Scene loading/management",
                "LevelManager|Level management",
                "GameManager|Game state management",
                "GameController|Main game controller",
                "Bootstrap|Initialization entry point",
                "Main|Main entry point",
                "EntryPoint|Explicit entry point",
                "ApplicationInitializer|App initialization"
            };

            entryPointExcludePaths = new string[]
            {
                "Tests", "Editor", "Plugins", "ThirdParty"
            };

            managerClassPatterns = new string[]
            {
                "Manager", "Controller", "Service", "System", "Handler"
            };

            managerExcludePrefixes = new string[]
            {
                "Unity", "Editor", "Test"
            };
        }

        /// <summary>
        /// Disables all pattern-based detection (fully custom mode).
        /// </summary>
        public void DisablePatterns()
        {
            entryPointPatterns = new string[0];
            entryPointExcludePaths = new string[0];
            managerClassPatterns = new string[0];
            managerExcludePrefixes = new string[0];
        }

        private Dictionary<string, string> ParseEntryPointPatterns()
        {
            var result = new Dictionary<string, string>();
            if (entryPointPatterns == null) return result;

            foreach (var entry in entryPointPatterns)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;

                var parts = entry.Split('|');
                if (parts.Length >= 2)
                {
                    result[parts[0].Trim()] = parts[1].Trim();
                }
                else if (parts.Length == 1)
                {
                    result[parts[0].Trim()] = "Entry point";
                }
            }
            return result;
        }

        // ========== Singleton Access ==========

        /// <summary>
        /// Gets or creates the singleton settings instance.
        /// Automatically creates the asset at Assets/ProjectSnapshotSettings.asset if it doesn't exist.
        /// </summary>
        public static ProjectSnapshotSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadSettings();
                    if (_instance == null)
                    {
                        _instance = CreateSettings();
                    }
                }
                return _instance;
            }
        }

        // ========== Persistence ==========

        /// <summary>
        /// Reloads settings from disk.
        /// </summary>
        public static void Reload()
        {
            _instance = LoadSettings();
        }

        private static ProjectSnapshotSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<ProjectSnapshotSettings>(SettingsPath);
        }

        private static ProjectSnapshotSettings CreateSettings()
        {
            var settings = CreateInstance<ProjectSnapshotSettings>();
            // Apply default patterns
            settings.EnableDefaultPatterns();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSnapshotSettings] Created new settings at {SettingsPath}");
            return settings;
        }

        /// <summary>
        /// Saves the current settings to disk.
        /// </summary>
        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSnapshotSettings] Settings saved");
        }

        /// <summary>
        /// Shows the settings inspector window.
        /// </summary>
        public static void ShowSettingsWindow()
        {
            Selection.activeObject = Instance;
            EditorApplication.ExecuteMenuItem("Window/General/Inspector");
        }

        /// <summary>
        /// Validates settings and returns any issues.
        /// </summary>
        public List<string> Validate()
        {
            var issues = new List<string>();

            if (maxDependenciesPerPrefab > maxPrefabsToAnalyze)
                issues.Add("maxDependenciesPerPrefab should not exceed maxPrefabsToAnalyze");

            if (maxCorePrefabs > maxPrefabsToAnalyze)
                issues.Add("maxCorePrefabs should not exceed maxPrefabsToAnalyze");

            if (string.IsNullOrEmpty(outputPath))
                issues.Add("outputPath cannot be empty");

            return issues;
        }
    }
}
