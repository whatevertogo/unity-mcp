using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Newtonsoft.Json;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Semantic levels for the three-tier pyramid model.
    /// Level 1: Core Semantic (Scripts, ScriptableObjects)
    /// Level 2: Structural (Prefabs, Scenes)
    /// Level 3: Raw Resources (Textures, Materials, Audio, Models)
    /// </summary>
    public static class SemanticLevels
    {
        public const int CORE_SEMANTIC = 1;      // Scripts, ScriptableObjects
        public const int STRUCTURAL = 2;          // Prefabs, Scenes
        public const int RAW_RESOURCE = 3;        // Textures, Audio, Models

        /// <summary>
        /// Weight coefficients for reference count calculation.
        /// Prevents "false hotspots" from shared resources like Default-Material.
        /// </summary>
        public static float GetWeightCoefficient(int semanticLevel)
        {
            return semanticLevel switch
            {
                CORE_SEMANTIC => 1.0f,
                STRUCTURAL => 1.0f,
                RAW_RESOURCE => 0.1f,
                _ => 1.0f
            };
        }

        /// <summary>
        /// Determines the semantic level from asset type.
        /// </summary>
        public static int GetSemanticLevel(string assetType)
        {
            return assetType switch
            {
                "Script" or "ScriptableObject" => CORE_SEMANTIC,
                "Prefab" or "Scene" => STRUCTURAL,
                "Texture" or "Material" or "Audio" or "Model" => RAW_RESOURCE,
                _ => STRUCTURAL  // Default to structural
            };
        }
    }

    /// <summary>
    /// Manages dependency indexing for fast asset dependency queries.
    /// </summary>
    public class DependencyIndex
    {
        private const int IndexVersion = 1;

        /// <summary>
        /// Cache for code summaries to avoid repeated file reads.
        /// Key: asset path, Value: (summary, lastModified)
        /// </summary>
        private Dictionary<string, (string Summary, DateTime LastModified)> _codeSummaryCache
            = new();

        /// <summary>
        /// In-memory index mapping asset paths to their index entries.
        /// </summary>
        private Dictionary<string, DependencyIndexEntry> _assetIndex;

        /// <summary>
        /// Reverse dependency index for finding assets that depend on a given asset.
        /// </summary>
        private Dictionary<string, List<string>> _reverseDependencyIndex;

        /// <summary>
        /// Type-based index for finding assets by type.
        /// </summary>
        private Dictionary<string, List<string>> _typeIndex;

        /// <summary>
        /// When the index was last generated.
        /// </summary>
        public DateTime LastGenerated { get; private set; }

        /// <summary>
        /// Number of assets in the index.
        /// </summary>
        public int Count => _assetIndex?.Count ?? 0;

        /// <summary>
        /// Whether the index has been loaded/generated.
        /// </summary>
        public bool IsLoaded => _assetIndex != null;

        /// <summary>
        /// Generates a new dependency index from project prefabs.
        /// </summary>
        public void GenerateIndex(SnapshotOptions options)
        {
            _assetIndex = new Dictionary<string, DependencyIndexEntry>();
            _reverseDependencyIndex = new Dictionary<string, List<string>>();
            _typeIndex = new Dictionary<string, List<string>>();

            var prefabs = AssetRegistryAnalyzer.FindCorePrefabs(options);

            foreach (var prefabInfo in prefabs)
            {
                var entry = new DependencyIndexEntry
                {
                    AssetPath = prefabInfo.Path,
                    AssetGuid = AssetDatabase.AssetPathToGUID(prefabInfo.Path),
                    AssetType = "Prefab",
                    DependencyPaths = prefabInfo.Dependencies.Select(d => d.Path).ToList()
                };

                _assetIndex[prefabInfo.Path] = entry;

                // Build reverse index
                foreach (var dep in prefabInfo.Dependencies)
                {
                    if (!_reverseDependencyIndex.ContainsKey(dep.Path))
                    {
                        _reverseDependencyIndex[dep.Path] = new List<string>();
                    }
                    _reverseDependencyIndex[dep.Path].Add(prefabInfo.Path);
                }

                // Build type index
                if (!_typeIndex.ContainsKey("Prefab"))
                {
                    _typeIndex["Prefab"] = new List<string>();
                }
                _typeIndex["Prefab"].Add(prefabInfo.Path);
            }

            // Also index other asset types
            IndexAdditionalAssetTypes(options);

            LastGenerated = DateTime.UtcNow;
        }

        /// <summary>
        /// Indexes additional asset types beyond prefabs.
        /// </summary>
        private void IndexAdditionalAssetTypes(SnapshotOptions options)
        {
            var assetsPath = Application.dataPath;

            // Index materials
            try
            {
                var materials = Directory.GetFiles(assetsPath, "*.mat", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package"))
                    .Take(options.MaxPrefabsToAnalyze > 0 ? options.MaxPrefabsToAnalyze : 100);

                foreach (var mat in materials)
                {
                    var relativePath = "Assets" + mat.Replace(assetsPath, "").Replace('\\', '/');
                    AddAssetToIndex(relativePath, "Material");
                }
            }
            catch { }

            // Index textures
            try
            {
                var textureExts = new[] { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif" };
                foreach (var ext in textureExts)
                {
                    var textures = Directory.GetFiles(assetsPath, "*" + ext, SearchOption.AllDirectories)
                        .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package"))
                        .Take(options.MaxPrefabsToAnalyze > 0 ? options.MaxPrefabsToAnalyze / 2 : 50);

                    foreach (var tex in textures)
                    {
                        var relativePath = "Assets" + tex.Replace(assetsPath, "").Replace('\\', '/');
                        AddAssetToIndex(relativePath, "Texture");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Adds an asset to the index.
        /// </summary>
        private void AddAssetToIndex(string path, string type)
        {
            if (_assetIndex.ContainsKey(path)) return;

            var entry = new DependencyIndexEntry
            {
                AssetPath = path,
                AssetGuid = AssetDatabase.AssetPathToGUID(path),
                AssetType = type,
                DependencyPaths = new List<string>()
            };

            _assetIndex[path] = entry;

            if (!_typeIndex.ContainsKey(type))
            {
                _typeIndex[type] = new List<string>();
            }
            _typeIndex[type].Add(path);
        }

        /// <summary>
        /// Queries the index for dependencies of a specific asset.
        /// </summary>
        public DependencyQueryResult Query(string assetPath, bool includeDependents = false)
        {
            if (!IsLoaded)
            {
                return null;
            }

            _assetIndex.TryGetValue(assetPath, out var entry);

            // If not in index, try to get basic info
            if (entry == null && AssetDatabase.AssetPathToGUID(assetPath) != string.Empty)
            {
                entry = new DependencyIndexEntry
                {
                    AssetPath = assetPath,
                    AssetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                    AssetType = Utilities.GetAssetTypeFromPath(assetPath),
                    DependencyPaths = new List<string>()
                };
            }

            if (entry == null)
            {
                return null;
            }

            var result = new DependencyQueryResult
            {
                AssetPath = assetPath,
                AssetType = entry.AssetType,
                FromCache = true
            };

            // Get dependencies
            foreach (var depPath in entry.DependencyPaths)
            {
                result.Dependencies.Add(new DependencyInfo
                {
                    Path = depPath,
                    Type = Utilities.GetAssetTypeFromPath(depPath),
                    Guid = AssetDatabase.AssetPathToGUID(depPath)
                });
            }
            result.TotalDependencies = result.Dependencies.Count;

            // Get dependents if requested
            if (includeDependents && _reverseDependencyIndex.TryGetValue(assetPath, out var dependents))
            {
                foreach (var depPath in dependents)
                {
                    result.Dependents.Add(new DependencyInfo
                    {
                        Path = depPath,
                        Type = "Prefab",
                        Guid = AssetDatabase.AssetPathToGUID(depPath)
                    });
                }
                result.TotalDependents = result.Dependents.Count;
            }

            return result;
        }

        /// <summary>
        /// Query dependencies with context-aware filtering for AI.
        /// Only returns relevant nodes to minimize token usage.
        /// Includes global statistics to prevent blind spots.
        /// </summary>
        /// <param name="focusPath">The center asset to query around</param>
        /// <param name="maxDepth">Depth of dependency graph (1 = direct, 2 = indirect)</param>
        /// <param name="includeCode">Whether to include code snippets (lazy extraction)</param>
        /// <param name="strategy">Query strategy: balanced/deep/slim</param>
        /// <returns>Optimized result for AI consumption</returns>
        public ContextualQueryResult QueryContextual(
            string focusPath,
            int maxDepth = 1,
            bool includeCode = false,
            QueryStrategy strategy = QueryStrategy.Balanced)
        {
            if (!IsLoaded)
            {
                return null;
            }

            var result = new ContextualQueryResult
            {
                FocusPath = focusPath,
                MaxDepth = maxDepth,
                Timestamp = DateTime.UtcNow
            };

            // 1. Get the focus asset
            if (!_assetIndex.TryGetValue(focusPath, out var focusEntry))
            {
                // Asset not in index, try to get basic info
                focusEntry = CreateEntryForAsset(focusPath);
                if (focusEntry == null) return null;
            }

            result.FocusedAsset = SummarizeEntry(focusEntry, includeCode);

            // 2. Get direct dependencies
            if (maxDepth >= 1)
            {
                foreach (var depPath in focusEntry.DependencyPaths)
                {
                    // Filter Level 3 resources based on strategy
                    if (_assetIndex.TryGetValue(depPath, out var depEntry))
                    {
                        var semanticLevel = SemanticLevels.GetSemanticLevel(depEntry.AssetType);
                        if (strategy == QueryStrategy.Slim && semanticLevel == SemanticLevels.RAW_RESOURCE)
                            continue;

                        var includeDepCode = includeCode && semanticLevel == SemanticLevels.CORE_SEMANTIC;
                        result.Dependencies.Add(SummarizeEntry(depEntry, includeDepCode));
                    }
                    else
                    {
                        result.Dependencies.Add(CreateMinimalSummary(depPath));
                    }
                }

                // 3. Get direct dependents (reverse dependencies - more important for AI)
                if (_reverseDependencyIndex.TryGetValue(focusPath, out var dependents))
                {
                    foreach (var depPath in dependents.Take(20))
                    {
                        if (_assetIndex.TryGetValue(depPath, out var depEntry))
                        {
                            result.Dependents.Add(SummarizeEntry(depEntry, false));
                        }
                        else
                        {
                            result.Dependents.Add(CreateMinimalSummary(depPath));
                        }
                    }
                }
            }

            // 4. Build global statistics (prevents blind spots)
            result.GlobalStats = BuildGlobalStats(maxDepth);

            return result;
        }

        /// <summary>
        /// Builds global statistics to inform AI about the full project scope.
        /// </summary>
        private GlobalStats BuildGlobalStats(int currentDepth)
        {
            var totalAssets = _assetIndex.Count;
            var hint = currentDepth >= 2
                ? "Showing deep relationships. Most dependencies are included."
                : $"Showing direct relationships only. {totalAssets} total assets in index. Use maxDepth=2 for deeper analysis.";

            // Calculate weighted hotness
            var hotAssets = new List<HotAsset>();
            foreach (var entry in _assetIndex.Values)
            {
                hotAssets.Add(new HotAsset
                {
                    Path = entry.AssetPath,
                    Type = entry.AssetType,
                    ReferenceCount = _reverseDependencyIndex.TryGetValue(entry.AssetPath, out var deps)
                        ? deps.Count
                        : 0,
                    WeightedScore = CalculateWeightedScore(entry)
                });
            }

            var topHot = hotAssets
                .OrderByDescending(h => h.WeightedScore)
                .Take(10)
                .ToList();

            return new GlobalStats
            {
                TotalAssets = totalAssets,
                TopHotAssets = topHot,
                DepthHint = hint
            };
        }

        /// <summary>
        /// Calculates weighted reference score to avoid false hotspots.
        /// </summary>
        private float CalculateWeightedScore(DependencyIndexEntry entry)
        {
            var refCount = _reverseDependencyIndex.TryGetValue(entry.AssetPath, out var deps)
                ? deps.Count
                : 0;
            var semanticLevel = SemanticLevels.GetSemanticLevel(entry.AssetType);
            var weight = SemanticLevels.GetWeightCoefficient(semanticLevel);
            return refCount * weight;
        }

        /// <summary>
        /// Creates an optimized summary for AI consumption.
        /// </summary>
        private AssetSummary SummarizeEntry(DependencyIndexEntry entry, bool includeCode)
        {
            var semanticLevel = SemanticLevels.GetSemanticLevel(entry.AssetType);
            var refCount = _reverseDependencyIndex.TryGetValue(entry.AssetPath, out var deps)
                ? deps.Count
                : 0;

            var summary = new AssetSummary
            {
                Path = entry.AssetPath,
                Type = entry.AssetType,
                SemanticLevel = semanticLevel,
                ReferenceCount = refCount,
                WeightedScore = refCount * SemanticLevels.GetWeightCoefficient(semanticLevel),
                Exists = true,
                LastUpdated = File.Exists(Path.Combine(Application.dataPath, "..", entry.AssetPath))
                    ? File.GetLastWriteTime(Path.Combine(Application.dataPath, "..", entry.AssetPath))
                    : DateTime.MinValue
            };

            // Extract code snippet for scripts if requested
            if (includeCode && entry.AssetType == "Script")
            {
                summary.CodeSnippet = ExtractCodeSummary(entry.AssetPath);
            }

            return summary;
        }

        /// <summary>
        /// Creates a minimal asset summary for token efficiency.
        /// </summary>
        private AssetSummary CreateMinimalSummary(string assetPath)
        {
            var assetType = Utilities.GetAssetTypeFromPath(assetPath);
            var semanticLevel = SemanticLevels.GetSemanticLevel(assetType);
            var refCount = _reverseDependencyIndex.TryGetValue(assetPath, out var deps)
                ? deps.Count
                : 0;

            return new AssetSummary
            {
                Path = assetPath,
                Type = assetType,
                SemanticLevel = semanticLevel,
                ReferenceCount = refCount,
                WeightedScore = refCount * SemanticLevels.GetWeightCoefficient(semanticLevel),
                Exists = File.Exists(Path.Combine(Application.dataPath, "..", assetPath))
            };
        }

        /// <summary>
        /// Creates an index entry for an asset not currently in the index.
        /// </summary>
        private DependencyIndexEntry CreateEntryForAsset(string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return null;

            return new DependencyIndexEntry
            {
                AssetPath = assetPath,
                AssetGuid = guid,
                AssetType = Utilities.GetAssetTypeFromPath(assetPath),
                DependencyPaths = new List<string>()
            };
        }

        /// <summary>
        /// Extracts a concise code summary using regex (fast, sufficient for 80% of cases).
        /// Lazy-extracted and cached for performance.
        /// </summary>
        /// <param name="scriptPath">Path to the script</param>
        /// <param name="forceRefresh">Force re-extraction even if cached</param>
        /// <returns>Code summary with public signatures only</returns>
        private string ExtractCodeSummary(string scriptPath, bool forceRefresh = false)
        {
            try
            {
                var fullPath = Path.Combine(Application.dataPath, "..", scriptPath);
                if (!File.Exists(fullPath)) return null;

                var lastModified = File.GetLastWriteTime(fullPath);

                // Check cache
                if (!forceRefresh && _codeSummaryCache.TryGetValue(scriptPath, out var cached))
                {
                    if (cached.LastModified >= lastModified)
                        return cached.Summary;
                }

                // Extract using regex (fast, sufficient for 80% of cases)
                var content = File.ReadAllText(fullPath);
                var summary = ExtractCodeSummaryRegex(content);

                // Cache result
                _codeSummaryCache[scriptPath] = (summary, lastModified);

                return summary;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fast regex-based code summary extraction.
        /// Captures class declarations, inheritance, interfaces, and public methods.
        /// </summary>
        private string ExtractCodeSummaryRegex(string content)
        {
            var summary = new System.Text.StringBuilder();

            // Add metadata declaration
            summary.AppendLine("/* [SUMMARY ONLY] Private members and property logic omitted. Use read_file for full implementation. */");
            summary.AppendLine();

            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Extract class declaration lines (including inheritance and interfaces)
                if (Regex.IsMatch(trimmed, @"^(public|internal|protected|private|partial)?\s*(class|struct|interface|enum)\s+\w+"))
                {
                    summary.AppendLine(trimmed);
                }
                // Extract public methods
                else if (trimmed.StartsWith("public ") && trimmed.Contains("("))
                {
                    summary.AppendLine(trimmed);
                }
                // Extract SerializeField (important for Unity context)
                else if (trimmed.Contains("[SerializeField]") || trimmed.Contains("[Header("))
                {
                    summary.AppendLine(trimmed);
                }
                // Extract interface implementations
                else if (trimmed.Contains(" : I") && (trimmed.Contains("class") || trimmed.Contains(")")))
                {
                    summary.AppendLine(trimmed);
                }

                // Limit summary size
                if (summary.Length > 800) break;
            }

            return summary.Length > 0 ? summary.ToString() : null;
        }

        /// <summary>
        /// Searches for assets by name pattern.
        /// </summary>
        public List<DependencyIndexEntry> SearchByName(string namePattern, int maxResults = 20)
        {
            if (!IsLoaded) return new List<DependencyIndexEntry>();

            var results = _assetIndex.Values
                .Where(e => Path.GetFileNameWithoutExtension(e.AssetPath)
                    .IndexOf(namePattern, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(maxResults)
                .ToList();

            return results;
        }

        /// <summary>
        /// Gets all assets of a specific type.
        /// </summary>
        public List<string> GetAssetsByType(string assetType)
        {
            if (!IsLoaded) return new List<string>();

            return _typeIndex.TryGetValue(assetType, out var assets) ? assets : new List<string>();
        }

        /// <summary>
        /// Gets all assets that depend on the specified asset (reverse dependency query).
        /// This is useful for finding which prefabs/scenes use a specific asset.
        /// </summary>
        /// <param name="assetPath">Path to the asset to find dependents for</param>
        /// <returns>List of asset paths that depend on the given asset</returns>
        public List<string> GetDependents(string assetPath)
        {
            if (!IsLoaded) return new List<string>();

            if (_reverseDependencyIndex.TryGetValue(assetPath, out var dependents))
            {
                return new List<string>(dependents); // Return a copy
            }

            return new List<string>();
        }

        /// <summary>
        /// Gets detailed dependent information for an asset.
        /// </summary>
        /// <param name="assetPath">Path to the asset to find dependents for</param>
        /// <returns>List of DependencyInfo for assets that depend on the given asset</returns>
        public List<DependencyInfo> GetDependentsDetailed(string assetPath)
        {
            if (!IsLoaded) return new List<DependencyInfo>();

            var result = new List<DependencyInfo>();

            if (_reverseDependencyIndex.TryGetValue(assetPath, out var dependents))
            {
                foreach (var depPath in dependents)
                {
                    result.Add(new DependencyInfo
                    {
                        Path = depPath,
                        Type = Utilities.GetAssetTypeFromPath(depPath),
                        Guid = AssetDatabase.AssetPathToGUID(depPath)
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Saves the index to disk.
        /// </summary>
        public bool SaveIndex(string indexPath)
        {
            try
            {
                var data = new
                {
                    version = IndexVersion,
                    timestamp = LastGenerated.ToString("o"),
                    assetCount = _assetIndex.Count,
                    assets = _assetIndex,
                    reverseDependencies = _reverseDependencyIndex,
                    typeIndex = _typeIndex
                };

                var json = JsonConvert.SerializeObject(data, Formatting.Indented);
                var directory = Path.GetDirectoryName(indexPath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(indexPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads the index from disk.
        /// </summary>
        public bool LoadIndex(string indexPath)
        {
            if (!File.Exists(indexPath)) return false;

            try
            {
                var json = File.ReadAllText(indexPath);
                var data = JsonConvert.DeserializeAnonymousType(json, new
                {
                    version = 0,
                    timestamp = string.Empty,
                    assetCount = 0,
                    assets = new Dictionary<string, DependencyIndexEntry>(),
                    reverseDependencies = new Dictionary<string, List<string>>(),
                    typeIndex = new Dictionary<string, List<string>>()
                });

                // Check version
                if (data.version != IndexVersion) return false;

                _assetIndex = data.assets;
                _reverseDependencyIndex = data.reverseDependencies;
                _typeIndex = data.typeIndex;

                if (DateTime.TryParse(data.timestamp, out var timestamp))
                {
                    LastGenerated = timestamp;
                }
                else
                {
                    LastGenerated = DateTime.UtcNow;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clears the index and code summary cache.
        /// </summary>
        public void Clear()
        {
            _assetIndex = null;
            _reverseDependencyIndex = null;
            _typeIndex = null;
            _codeSummaryCache = null;
            LastGenerated = DateTime.MinValue;
        }

        /// <summary>
        /// Gets the default index path.
        /// </summary>
        public static string GetDefaultIndexPath()
        {
            return Path.Combine(Application.dataPath, "..", "Library", "ProjectSnapshot", ".index");
        }
    }
}
