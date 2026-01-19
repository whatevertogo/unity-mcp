using System;
using System.Collections.Generic;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Data models for Project Snapshot generation.
    /// </summary>

    /// <summary>
    /// Configurable patterns for detecting project elements.
    /// All patterns are user-defined; no defaults are applied.
    /// </summary>
    public class SnapshotPatterns
    {
        /// <summary>
        /// Entry point detection patterns. Key = filename pattern, Value = description.
        /// If null or empty, entry point detection is skipped.
        /// </summary>
        public Dictionary<string, string> EntryPointPatterns { get; set; }

        /// <summary>
        /// Path patterns to exclude when detecting entry points.
        /// Scripts matching these path fragments are excluded from entry point detection.
        /// Default: { "Tests", "Editor", "Plugins", "ThirdParty" }
        /// </summary>
        public string[] EntryPointExcludePaths { get; set; }

        /// <summary>
        /// Manager class suffix patterns for detection.
        /// If null or empty, manager class detection is skipped.
        /// </summary>
        public string[] ManagerClassPatterns { get; set; }

        /// <summary>
        /// Prefixes to exclude when detecting manager classes.
        /// If null, no exclusions are applied.
        /// </summary>
        public string[] ManagerExcludePrefixes { get; set; }
    }

    /// <summary>
    /// Snapshot generation options.
    /// Only basic defaults are provided; all analysis patterns and limits are user-configurable.
    /// </summary>
    public class SnapshotOptions
    {
        /// <summary>
        /// Include package directories in snapshot.
        /// </summary>
        public bool IncludePackages { get; set; } = false;

        /// <summary>
        /// Maximum directory depth to scan (0 = unlimited).
        /// </summary>
        public int MaxDepth { get; set; } = 4;

        /// <summary>
        /// Include dependency analysis for prefabs.
        /// </summary>
        public bool IncludeDependencies { get; set; } = true;

        /// <summary>
        /// Include data schema examples (now enabled by default for better AI understanding).
        /// </summary>
        public bool IncludeDataSchemas { get; set; } = true;

        /// <summary>
        /// Output file path (relative to Assets folder).
        /// </summary>
        public string OutputPath { get; set; } = "Project_Snapshot.md";

        // === Analysis Limits (0 = unlimited) ===

        /// <summary>
        /// Maximum number of prefabs to analyze for dependencies (0 = unlimited).
        /// </summary>
        public int MaxPrefabsToAnalyze { get; set; }

        /// <summary>
        /// Maximum number of core prefabs to include in output (0 = unlimited).
        /// </summary>
        public int MaxCorePrefabs { get; set; }

        /// <summary>
        /// Maximum dependencies per prefab to show (0 = unlimited).
        /// </summary>
        public int MaxDependenciesPerPrefab { get; set; }

        /// <summary>
        /// Maximum manager classes to list (0 = unlimited).
        /// </summary>
        public int MaxManagerClasses { get; set; }

        /// <summary>
        /// Maximum ScriptableObject types to extract (0 = unlimited).
        /// </summary>
        public int MaxScriptableObjects { get; set; }

        /// <summary>
        /// Maximum C# files to scan for ScriptableObjects (0 = unlimited).
        /// </summary>
        public int MaxFilesToScan { get; set; }

        /// <summary>
        /// Maximum JSON files to include as examples (0 = unlimited).
        /// </summary>
        public int MaxJsonExamples { get; set; }

        /// <summary>
        /// Configurable patterns for detecting project elements.
        /// If null, pattern-based detection is skipped.
        /// </summary>
        public SnapshotPatterns Patterns { get; set; }

        // === Cache and Output Options ===

        /// <summary>
        /// Use cached data if available.
        /// </summary>
        public bool UseCache { get; set; } = true;

        /// <summary>
        /// Force regeneration even if cache is valid.
        /// </summary>
        public bool ForceRegenerate { get; set; } = false;

        /// <summary>
        /// Output path for the separate dependencies file.
        /// </summary>
        public string DependenciesOutputPath { get; set; } = "Asset_Dependencies.md";

        /// <summary>
        /// Generate dependency index for fast queries.
        /// </summary>
        public bool GenerateIndex { get; set; } = true;

        /// <summary>
        /// Path to the cache metadata file (stored in Library/ProjectSnapshot folder).
        /// </summary>
        public string CachePath { get; set; } = "Library/ProjectSnapshot/.cache";

        /// <summary>
        /// Path to the dependency index file (stored in Library/ProjectSnapshot folder).
        /// </summary>
        public string IndexPath { get; set; } = "Library/ProjectSnapshot/.index";

        /// <summary>
        /// Cache validity duration in minutes (0 = unlimited).
        /// </summary>
        public int CacheValidityMinutes { get; set; } = 60;

        /// <summary>
        /// Separate dependencies into independent file.
        /// </summary>
        public bool SeparateDependenciesFile { get; set; } = true;

        // === Smart Folding Options ===

        /// <summary>
        /// Enable smart directory folding (default: true).
        /// Resource-heavy folders (Textures, Materials, Audio, etc.) will be collapsed to summary view.
        /// </summary>
        public bool EnableSmartFolding { get; set; } = true;

        /// <summary>
        /// File count threshold for triggering folder folding (default: 30).
        /// Folders with more non-script files than this threshold will be collapsed.
        /// </summary>
        public int FoldingThreshold { get; set; } = 30;

        // === Circuit Breaker Options ===

        /// <summary>
        /// Maximum estimated token budget for the main snapshot (default: 5000).
        /// Content generation will be truncated if this limit is exceeded.
        /// </summary>
        public int MaxSnapshotTokens { get; set; } = 5000;

        /// <summary>
        /// Maximum estimated token budget for the dependencies file (default: 8000).
        /// </summary>
        public int MaxDependencyTokens { get; set; } = 8000;

        /// <summary>
        /// Maximum number of prefabs to include in the snapshot (default: 200).
        /// Prefabs are prioritized by importance; less important ones are excluded first.
        /// </summary>
        public int MaxPrefabsInSnapshot { get; set; } = 200;

        // === Priority Settings ===

        /// <summary>
        /// Core naming keywords for identifying important prefabs.
        /// Prefabs with these keywords in their name get higher priority.
        /// </summary>
        public string[] CoreNamingKeywords { get; set; } = new[]
        {
            "manager", "controller", "handler", "system",
            "core", "main", "game", "player", "ui"
        };

        /// <summary>
        /// Number of top dependencies to show per prefab (default: 3).
        /// If a prefab has more dependencies, only the top 3 by priority are shown.
        /// </summary>
        public int TopDependenciesToShow { get; set; } = 3;
    }

    /// <summary>
    /// Prefab dependency information.
    /// </summary>
    public class PrefabInfo
    {
        public string Path { get; set; }
        public List<(string Type, string Path)> Dependencies { get; set; } = new();

        // === Priority calculation fields ===

        /// <summary>
        /// Number of other assets that reference this prefab (reverse dependency count).
        /// Used for priority calculation.
        /// </summary>
        public int? ReferenceCount { get; set; }

        /// <summary>
        /// List of scene paths where this prefab is used.
        /// Used for priority calculation.
        /// </summary>
        public List<string> UsedInScenes { get; set; } = new();

        /// <summary>
        /// Calculated priority score (higher = more important).
        /// Set by PrioritizePrefabs() method.
        /// </summary>
        public int PriorityScore { get; set; }

        /// <summary>
        /// Whether this prefab has a parent prefab (Prefab Variant).
        /// Used for dependency display prioritization.
        /// </summary>
        public bool HasParentPrefab { get; set; }

        /// <summary>
        /// Path to the parent prefab if this is a Prefab Variant.
        /// </summary>
        public string ParentPrefabPath { get; set; }
    }

    /// <summary>
    /// ScriptableObject data type information.
    /// </summary>
    public class ScriptableObjectInfo
    {
        public string Path { get; set; }
        public string ClassName { get; set; }
        public string CodeSnippet { get; set; }
    }

    /// <summary>
    /// JSON file information.
    /// </summary>
    public class JsonFileInfo
    {
        public string Path { get; set; }
        public string Content { get; set; }
    }

    /// <summary>
    /// Architecture type detection result.
    /// </summary>
    public class ArchitectureType
    {
        public string Type { get; set; }
        public string Confidence { get; set; }
    }

    /// <summary>
    /// Loading strategy detection result.
    /// </summary>
    public class LoadingStrategy
    {
        public string Method { get; set; }
        public List<string> Indicators { get; set; } = new();
    }

    /// <summary>
    /// Entry point information.
    /// </summary>
    public class EntryPoint
    {
        public string Path { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Snapshot cache metadata for tracking project state and cache validity.
    /// </summary>
    public class SnapshotCacheMetadata
    {
        /// <summary>
        /// When the snapshot was last generated.
        /// </summary>
        public DateTime LastGenerated { get; set; }

        /// <summary>
        /// When the project was last checked for modifications.
        /// </summary>
        public DateTime LastModifiedCheck { get; set; }

        /// <summary>
        /// The last modified time of the project in ticks.
        /// </summary>
        public long ProjectLastModifiedTicks { get; set; }

        /// <summary>
        /// Hash of the project architecture for change detection.
        /// </summary>
        public string ArchitectureHash { get; set; }

        /// <summary>
        /// Hash of the directory structure for change detection.
        /// </summary>
        public string DirectoryHash { get; set; }

        /// <summary>
        /// Whether the snapshot includes dependency information.
        /// </summary>
        public bool HasDependencies { get; set; }

        /// <summary>
        /// Total number of assets analyzed.
        /// </summary>
        public int TotalAssets { get; set; }

        /// <summary>
        /// Total number of prefabs analyzed.
        /// </summary>
        public int TotalPrefabs { get; set; }

        /// <summary>
        /// Version of the cache format for compatibility.
        /// </summary>
        public int CacheVersion { get; set; } = 1;
    }

    /// <summary>
    /// Entry in the dependency index for fast lookups.
    /// </summary>
    public class DependencyIndexEntry
    {
        /// <summary>
        /// Path to the asset.
        /// </summary>
        public string AssetPath { get; set; }

        /// <summary>
        /// Unity GUID of the asset.
        /// </summary>
        public string AssetGuid { get; set; }

        /// <summary>
        /// Type of the asset (Prefab, Material, etc.).
        /// </summary>
        public string AssetType { get; set; }

        /// <summary>
        /// List of assets this asset depends on.
        /// </summary>
        public List<string> DependencyPaths { get; set; } = new();

        /// <summary>
        /// List of assets that depend on this asset (reverse dependencies).
        /// </summary>
        public List<string> DependentPaths { get; set; } = new();
    }

    /// <summary>
    /// Result of a dependency query.
    /// </summary>
    public class DependencyQueryResult
    {
        /// <summary>
        /// Path of the queried asset.
        /// </summary>
        public string AssetPath { get; set; }

        /// <summary>
        /// Type of the queried asset.
        /// </summary>
        public string AssetType { get; set; }

        /// <summary>
        /// Dependencies of this asset.
        /// </summary>
        public List<DependencyInfo> Dependencies { get; set; } = new();

        /// <summary>
        /// Assets that depend on this asset (reverse dependencies).
        /// </summary>
        public List<DependencyInfo> Dependents { get; set; } = new();

        /// <summary>
        /// Total number of dependencies.
        /// </summary>
        public int TotalDependencies { get; set; }

        /// <summary>
        /// Total number of dependents.
        /// </summary>
        public int TotalDependents { get; set; }

        /// <summary>
        /// Whether the result was retrieved from cache.
        /// </summary>
        public bool FromCache { get; set; }
    }

    /// <summary>
    /// Detailed dependency information.
    /// </summary>
    public class DependencyInfo
    {
        /// <summary>
        /// Path to the dependency.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Type of the dependency.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Unity GUID of the dependency.
        /// </summary>
        public string Guid { get; set; }
    }

    /// <summary>
    /// Result of checking if project needs snapshot regeneration.
    /// </summary>
    public class ProjectDirtyCheckResult
    {
        /// <summary>
        /// Whether the project has changed since last snapshot.
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Last project modification time (UTC).
        /// </summary>
        public DateTime LastProjectModified { get; set; }

        /// <summary>
        /// Last snapshot generation time (UTC).
        /// </summary>
        public DateTime LastSnapshotGenerated { get; set; }

        /// <summary>
        /// Recommendation for the user.
        /// </summary>
        public string Recommendation { get; set; }

        /// <summary>
        /// Areas that have changed (if available).
        /// </summary>
        public List<string> ChangedAreas { get; set; } = new();

        /// <summary>
        /// Age of the snapshot in minutes.
        /// </summary>
        public double SnapshotAgeMinutes { get; set; }
    }

    /// <summary>
    /// Result of a contextual query optimized for AI consumption.
    /// Returns only the focus asset and its direct relationships for minimal token usage.
    /// Includes global statistics to prevent AI blind spots.
    /// </summary>
    public class ContextualQueryResult
    {
        /// <summary>
        /// The focus asset that was queried.
        /// </summary>
        public AssetSummary FocusedAsset { get; set; }

        /// <summary>
        /// Direct dependencies of the focus asset (what it depends on).
        /// </summary>
        public List<AssetSummary> Dependencies { get; set; } = new();

        /// <summary>
        /// Assets that depend on the focus asset (reverse dependencies).
        /// Critical for AI to understand impact scope before making changes.
        /// </summary>
        public List<AssetSummary> Dependents { get; set; } = new();

        /// <summary>
        /// Global statistics to avoid AI blind spots.
        /// Informs AI about the overall project size even when returning partial data.
        /// </summary>
        public GlobalStats GlobalStats { get; set; }

        /// <summary>
        /// Query metadata.
        /// </summary>
        public string FocusPath { get; set; }
        public int MaxDepth { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Total number of nodes returned (for token estimation).
        /// </summary>
        public int TotalNodesReturned => 1 + Dependencies.Count + Dependents.Count;
    }

    /// <summary>
    /// Optimized asset summary for minimal token usage.
    /// Contains essential information without GUIDs or full payloads.
    /// </summary>
    public class AssetSummary
    {
        /// <summary>
        /// Asset path (e.g., "Assets/Prefabs/Player.prefab").
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Asset type (Prefab, Script, Material, Texture, etc.).
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Semantic level: 1=Core(Script), 2=Structural(Prefab), 3=Raw(Texture).
        /// Determines inclusion strategy in queries.
        /// </summary>
        public int SemanticLevel { get; set; }

        /// <summary>
        /// Number of assets referencing this asset (hotness indicator).
        /// Higher values indicate more critical/coupled assets.
        /// Weighted by semantic level to avoid "false hotspots".
        /// </summary>
        public int ReferenceCount { get; set; }

        /// <summary>
        /// Weighted reference score for AI risk assessment.
        /// Scripts/Prefabs: 1.0x, Textures/Materials: 0.1x
        /// </summary>
        public float WeightedScore { get; set; }

        /// <summary>
        /// Whether the asset exists in the project.
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// Code snippet summary (for scripts only, when includeCode=true).
        /// Lazy-extracted on first request.
        /// Contains only public signatures for AI understanding.
        /// </summary>
        public string CodeSnippet { get; set; }

        /// <summary>
        /// Last update timestamp for staleness detection.
        /// AI should verify with read_file if too old (> 7 days).
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Whether this asset is part of a circular reference.
        /// Critical for AI to understand refactoring risks.
        /// </summary>
        public bool IsCircular { get; set; }

        /// <summary>
        /// List of interface/base class names (for semantic connection).
        /// Helps AI discover cross-directory relationships.
        /// </summary>
        public List<string> SemanticLinks { get; set; } = new();

        /// <summary>
        /// Brief name without path (for readability).
        /// </summary>
        public string Name => Path != null ? Path.Substring(Path.LastIndexOf('/') + 1) : "";
    }

    /// <summary>
    /// Global statistics to inform AI about the full project scope.
    /// Prevents blind spots when AI only sees partial data.
    /// </summary>
    public class GlobalStats
    {
        /// <summary>
        /// Total number of assets in the index.
        /// </summary>
        public int TotalAssets { get; set; }

        /// <summary>
        /// Top N hottest assets by weighted reference count.
        /// </summary>
        public List<HotAsset> TopHotAssets { get; set; } = new();

        /// <summary>
        /// Hint to AI about whether more data is available.
        /// </summary>
        public string DepthHint { get; set; }
    }

    /// <summary>
    /// Hot asset information for risk assessment.
    /// Used for identifying critical/high-impact assets in the project.
    /// </summary>
    public class HotAsset
    {
        /// <summary>
        /// Asset path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Asset type.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Raw reference count (number of assets that depend on this).
        /// </summary>
        public int ReferenceCount { get; set; }

        /// <summary>
        /// Weighted score (ReferenceCount × SemanticLevelWeight).
        /// Prevents shared resources (like Default-Material) from appearing as "hot".
        /// </summary>
        public float WeightedScore { get; set; }
    }

    /// <summary>
    /// Query strategy for token optimization.
    /// </summary>
    public enum QueryStrategy
    {
        /// <summary>
        /// Default: Filter Level 3 resources, include code summaries for scripts.
        /// </summary>
        Balanced,

        /// <summary>
        /// Include all assets with full details.
        /// </summary>
        Deep,

        /// <summary>
        /// Return paths and reference counts only (minimal tokens).
        /// </summary>
        Slim
    }
}
