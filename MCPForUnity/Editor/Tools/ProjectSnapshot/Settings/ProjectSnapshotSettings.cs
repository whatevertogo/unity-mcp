using System;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Simplified settings for ProjectSnapshot auto-generation.
    /// Only essential options - remove complexity.
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectSnapshotSettings", menuName = "MCP/ProjectSnapshot Settings")]
    public sealed class ProjectSnapshotSettings : ScriptableObject
    {
        private const string SettingsPath = "Assets/ProjectSnapshotSettings.asset";
        private static ProjectSnapshotSettings _instance;

        #region Core Settings

        [Header("Core Settings")]
        [Tooltip("Enable automatic snapshot generation after script compilation")]
        public bool autoGenerateEnabled = true;

        [Tooltip("Output file name (relative to project root)")]
        public string outputPath = "Project_Snapshot.md";

        [Tooltip("Separate dependencies into independent file")]
        public bool separateDependenciesFile = true;

        [Tooltip("Dependencies file name")]
        public string dependenciesOutputPath = "Asset_Dependencies.md";

        #endregion

        #region Analysis Limits

        [Header("Analysis Limits (0 = unlimited)")]
        [Min(0)]
        public int maxPrefabsToAnalyze = 50;

        [Min(0)]
        public int maxDependenciesPerPrefab = 10;

        [Min(0)]
        public int maxScriptableObjects = 20;

        #endregion

        #region Singleton

        public static ProjectSnapshotSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadSettings() ?? CreateSettings();
                }
                return _instance;
            }
        }

        private static ProjectSnapshotSettings LoadSettings()
        {
            return AssetDatabase.LoadAssetAtPath<ProjectSnapshotSettings>(SettingsPath);
        }

        private static ProjectSnapshotSettings CreateSettings()
        {
            var settings = CreateInstance<ProjectSnapshotSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProjectSnapshotSettings] Created at {SettingsPath}");
            return settings;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region Conversion to Options

        public SnapshotOptions ToOptions()
        {
            return new SnapshotOptions
            {
                OutputPath = outputPath,
                SeparateDependenciesFile = separateDependenciesFile,
                DependenciesOutputPath = dependenciesOutputPath,
                MaxPrefabsToAnalyze = maxPrefabsToAnalyze,
                MaxDependenciesPerPrefab = maxDependenciesPerPrefab,
                MaxScriptableObjects = maxScriptableObjects,
                // Default values for omitted settings
                IncludePackages = false,
                MaxDepth = 4,
                IncludeDependencies = true,
                IncludeDataSchemas = true,
                UseCache = true,
                CacheValidityMinutes = 60,
                GenerateIndex = true,
                EnableSmartFolding = true,
                FoldingThreshold = 30,
                MaxSnapshotTokens = 5000,
                MaxDependencyTokens = 8000,
                MaxPrefabsInSnapshot = 200,
                TopDependenciesToShow = 3,
            };
        }

        #endregion
    }
}
