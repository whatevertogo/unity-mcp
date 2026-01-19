using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes prefabs, scenes, and their dependencies.
    /// Enhanced to support Scene and ScriptableObject dependency analysis.
    /// </summary>
    internal static class AssetRegistryAnalyzer
    {
        // Cache for asset analysis
        private static Dictionary<string, List<(string Type, string Path)>> _dependencyCache;
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private const int CACHE_VALIDITY_SECONDS = 180;

        /// <summary>
        /// Scene information with dependencies.
        /// </summary>
        public class SceneInfo
        {
            public string Path { get; set; }
            public string Name { get; set; }
            public int GameObjectCount { get; set; }
            public List<(string Type, string Path)> Dependencies { get; set; } = new();
            public bool IsBuildSettingsIncluded { get; set; }
            public int BuildIndex { get; set; } = -1;
        }

        /// <summary>
        /// ScriptableObject instance information.
        /// </summary>
        public class ScriptableObjectInstanceInfo
        {
            public string Path { get; set; }
            public string TypeName { get; set; }
            public List<(string Type, string Path)> Dependencies { get; set; } = new();
        }

        /// <summary>
        /// Generates the enhanced asset registry section for the snapshot.
        /// </summary>
        public static void GenerateAssetRegistry(StringBuilder sb, SnapshotOptions options)
        {
            sb.AppendLine("## [SECTION 4: Asset Registry & Dependency Tree]");
            sb.AppendLine();

            // Include scene information
            var scenes = FindCoreScenes(options);
            if (scenes.Count > 0)
            {
                sb.AppendLine("### Scenes");
                sb.AppendLine();
                foreach (var scene in scenes.Take(10))
                {
                    var buildInfo = scene.IsBuildSettingsIncluded ? $" [Build: {scene.BuildIndex}]" : "";
                    sb.AppendLine($"- **{scene.Name}**{buildInfo} ({scene.GameObjectCount} objects)");
                    if (scene.Dependencies.Count > 0 && scene.Dependencies.Count <= 10)
                    {
                        sb.AppendLine($"  Dependencies: {string.Join(", ", scene.Dependencies.Select(d => $"{Path.GetFileNameWithoutExtension(d.Path)} ({d.Type})"))}");
                    }
                    else if (scene.Dependencies.Count > 10)
                    {
                        sb.AppendLine($"  Dependencies: {scene.Dependencies.Count} assets");
                    }
                }
                sb.AppendLine();
            }

            // Include prefabs
            var prefabs = FindCorePrefabs(options);
            if (prefabs.Count > 0)
            {
                sb.AppendLine("### Core Prefabs");
                sb.AppendLine();
                sb.AppendLine($"Total prefabs analyzed: {prefabs.Count}");
                sb.AppendLine();

                foreach (var prefab in prefabs.Take(15))
                {
                    sb.AppendLine($"- `{Path.GetFileNameWithoutExtension(prefab.Path)}`");
                    if (prefab.Dependencies.Count > 0 && prefab.Dependencies.Count <= 5)
                    {
                        sb.AppendLine($"  - Dependencies: {string.Join(", ", prefab.Dependencies.Select(d => $"{Path.GetFileNameWithoutExtension(d.Path)} ({d.Type})"))}");
                    }
                }
                sb.AppendLine();
            }
        }

        /// <summary>
        /// Finds core scenes with their dependencies.
        /// </summary>
        public static List<SceneInfo> FindCoreScenes(SnapshotOptions options = null)
        {
            var scenes = new List<SceneInfo>();
            var assetsPath = Application.dataPath;
            var snapshotOptions = options ?? new SnapshotOptions();

            // Find all scene files
            var sceneFiles = Directory.GetFiles(assetsPath, "*.unity", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package"))
                .Take(snapshotOptions.MaxPrefabsToAnalyze > 0 ? snapshotOptions.MaxPrefabsToAnalyze : 50);

            // Get scenes in build settings
            var buildScenes = new HashSet<string>(EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path));

            foreach (var sceneFile in sceneFiles)
            {
                var relativePath = "Assets" + sceneFile.Replace(assetsPath, "").Replace('\\', '/');

                var sceneInfo = new SceneInfo
                {
                    Path = relativePath,
                    Name = Path.GetFileNameWithoutExtension(relativePath),
                    IsBuildSettingsIncluded = buildScenes.Contains(relativePath)
                };

                // Get build index if in build settings
                if (sceneInfo.IsBuildSettingsIncluded)
                {
                    for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                    {
                        if (EditorBuildSettings.scenes[i].path == relativePath)
                        {
                            sceneInfo.BuildIndex = i;
                            break;
                        }
                    }
                }

                // Get dependencies
                sceneInfo.Dependencies = GetAssetDependencies(relativePath, snapshotOptions);

                // Estimate game object count (from dependencies)
                // Use asset count as proxy: Prefabs + Meshes + Materials give a rough estimate
                var assetCount = sceneInfo.Dependencies.Count(d => d.Type == "Mesh" || d.Type == "Prefab" || d.Type == "Material");
                sceneInfo.GameObjectCount = assetCount > 0 ? assetCount : sceneInfo.Dependencies.Count;

                scenes.Add(sceneInfo);
            }

            // Sort: build settings scenes first, then by name
            scenes = scenes.OrderByDescending(s => s.IsBuildSettingsIncluded)
                .ThenBy(s => s.Name)
                .ToList();

            return scenes;
        }

        /// <summary>
        /// Finds core prefabs with their dependencies and scene usage.
        /// Enhanced with scene reference detection and prefab variant detection.
        /// </summary>
        public static List<PrefabInfo> FindCorePrefabs(SnapshotOptions options = null)
        {
            var prefabs = new List<PrefabInfo>();
            var assetsPath = Application.dataPath;
            var snapshotOptions = options ?? new SnapshotOptions();

            // Find all prefab files
            var prefabQuery = Directory.GetFiles(assetsPath, "*.prefab", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package"));

            // Apply limit if configured (0 = unlimited)
            var limit = snapshotOptions.MaxPrefabsToAnalyze;
            if (limit > 0)
            {
                prefabQuery = prefabQuery.Take(limit);
            }

            // Pre-load all scenes for reference lookup
            var allScenes = FindCoreScenes(snapshotOptions);

            foreach (var prefabFile in prefabQuery)
            {
                var relativePath = "Assets" + prefabFile.Replace(assetsPath, "").Replace('\\', '/');
                var info = new PrefabInfo
                {
                    Path = relativePath,
                    Dependencies = GetAssetDependencies(relativePath, snapshotOptions)
                };

                // Detect prefab variant (parent prefab)
                DetectPrefabVariant(info, relativePath);

                // Find which scenes use this prefab
                info.UsedInScenes = FindScenesUsingPrefab(relativePath, allScenes, snapshotOptions);

                // Only include prefabs with dependencies or in key locations
                if (info.Dependencies.Count > 0 ||
                    relativePath.Contains("Prefabs", StringComparison.OrdinalIgnoreCase))
                {
                    prefabs.Add(info);
                }
            }

            // Apply output limit if configured (0 = unlimited)
            var outputLimit = snapshotOptions.MaxCorePrefabs;
            if (outputLimit > 0)
            {
                prefabs = prefabs.Take(outputLimit).ToList();
            }

            return prefabs;
        }

        /// <summary>
        /// Detects if a prefab is a variant by checking for parent prefab reference.
        /// </summary>
        private static void DetectPrefabVariant(PrefabInfo info, string prefabPath)
        {
            try
            {
                // Load the prefab asset to check for parent
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null) return;

                // Check if it's a prefab variant using UnityEditor.PrefabUtility
#if UNITY_2021_1_OR_NEWER
                var prefabStatus = UnityEditor.PrefabUtility.GetPrefabInstanceStatus(prefabAsset);
                if (prefabStatus == PrefabInstanceStatus.Connected)
                {
                    var parentPrefab = UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefabAsset);
                    if (parentPrefab != null)
                    {
                        var parentPath = AssetDatabase.GetAssetPath(parentPrefab);
                        if (!string.IsNullOrEmpty(parentPath) && parentPath != prefabPath)
                        {
                            info.HasParentPrefab = true;
                            info.ParentPrefabPath = parentPath;
                        }
                    }
                }
#endif
            }
            catch
            {
                // Silently skip detection on error
            }
        }

        /// <summary>
        /// Finds all scenes that reference the given prefab.
        /// Uses dependency scanning to detect references.
        /// </summary>
        private static List<string> FindScenesUsingPrefab(string prefabPath, List<SceneInfo> scenes, SnapshotOptions options)
        {
            var usingScenes = new List<string>();

            try
            {
                // Get the prefab's GUID for reference lookup
                var prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
                if (string.IsNullOrEmpty(prefabGuid) || prefabGuid.StartsWith("00000"))
                    return usingScenes;

                // Check each scene's dependencies for this prefab reference
                foreach (var scene in scenes)
                {
                    // Method 1: Check direct dependencies
                    if (scene.Dependencies.Any(d => d.Path == prefabPath))
                    {
                        usingScenes.Add(scene.Path);
                        continue;
                    }

                    // Method 2: Check prefab dependencies (nested prefabs)
                    bool foundInNested = false;
                    foreach (var dep in scene.Dependencies.Where(d => d.Type == "Prefab"))
                    {
                        var nestedDeps = AssetDatabase.GetDependencies(dep.Path, recursive: true);
                        if (nestedDeps.Contains(prefabPath))
                        {
                            usingScenes.Add(scene.Path);
                            foundInNested = true;
                            break;
                        }
                    }

                    if (foundInNested) continue;
                }

                // Method 3: For more thorough detection, scan scene assets directly (limited)
                if (usingScenes.Count == 0 && options.MaxPrefabsToAnalyze > 0 && options.MaxPrefabsToAnalyze <= 100)
                {
                    foreach (var scene in scenes.Take(20)) // Limit search to 20 scenes
                    {
                        if (ScanSceneForPrefabReference(scene.Path, prefabGuid))
                        {
                            if (!usingScenes.Contains(scene.Path))
                            {
                                usingScenes.Add(scene.Path);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Silently skip on error
            }

            return usingScenes;
        }

        /// <summary>
        /// Scans a scene file for prefab reference by GUID.
        /// This is a more thorough but slower check.
        /// </summary>
        private static bool ScanSceneForPrefabReference(string scenePath, string prefabGuid)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", scenePath));
                if (!File.Exists(fullPath)) return false;

                var sceneContent = File.ReadAllText(fullPath);
                return sceneContent.Contains($"guid: {prefabGuid}") ||
                       sceneContent.Contains($"guid:{prefabGuid}");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Finds ScriptableObject instances in the project.
        /// </summary>
        public static List<ScriptableObjectInstanceInfo> FindScriptableObjectInstances(SnapshotOptions options = null)
        {
            var instances = new List<ScriptableObjectInstanceInfo>();
            var assetsPath = Application.dataPath;
            var snapshotOptions = options ?? new SnapshotOptions();

            // Find all .asset files (ScriptableObject instances)
            var assetFiles = Directory.GetFiles(assetsPath, "*.asset", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package") &&
                           !f.Contains("Resources")) // Exclude Resources as they're loaded dynamically
                .Take(snapshotOptions.MaxScriptableObjects > 0 ? snapshotOptions.MaxScriptableObjects * 2 : 20);

            foreach (var assetFile in assetFiles)
            {
                try
                {
                    var relativePath = "Assets" + assetFile.Replace(assetsPath, "").Replace('\\', '/');
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(relativePath);

                    if (assetType != null && typeof(ScriptableObject).IsAssignableFrom(assetType))
                    {
                        instances.Add(new ScriptableObjectInstanceInfo
                        {
                            Path = relativePath,
                            TypeName = assetType.Name,
                            Dependencies = GetAssetDependencies(relativePath, snapshotOptions)
                        });
                    }
                }
                catch
                {
                    // Skip assets that can't be loaded
                }
            }

            return instances;
        }

        /// <summary>
        /// Gets dependencies for a specific asset with caching.
        /// </summary>
        private static List<(string Type, string Path)> GetAssetDependencies(string assetPath, SnapshotOptions options)
        {
            // Check cache
            if (_dependencyCache != null && (DateTime.UtcNow - _lastCacheUpdate).TotalSeconds < CACHE_VALIDITY_SECONDS)
            {
                if (_dependencyCache.ContainsKey(assetPath))
                {
                    return _dependencyCache[assetPath].Take(options.MaxDependenciesPerPrefab > 0 ? options.MaxDependenciesPerPrefab : int.MaxValue).ToList();
                }
            }
            else
            {
                _dependencyCache = new Dictionary<string, List<(string Type, string Path)>>();
                _lastCacheUpdate = DateTime.UtcNow;
            }

            var dependencies = new List<(string, string)>();

            try
            {
                // Get all dependencies
                var deps = AssetDatabase.GetDependencies(assetPath, recursive: false);

                foreach (var dep in deps)
                {
                    if (dep == assetPath) continue;

                    string type = Utilities.GetAssetTypeFromPath(dep);
                    dependencies.Add((type, dep));
                }
            }
            catch
            {
                // Ignore errors silently
            }

            // Cache the result
            _dependencyCache[assetPath] = dependencies;

            // Apply limit if configured (0 = unlimited)
            var limit = options.MaxDependenciesPerPrefab;
            if (limit > 0)
            {
                dependencies = dependencies.Take(limit).ToList();
            }

            return dependencies;
        }

        /// <summary>
        /// Finds circular dependencies between assets.
        /// </summary>
        public static List<List<string>> FindCircularDependencies(SnapshotOptions options = null)
        {
            var cycles = new List<List<string>>();
            var assetsPath = Application.dataPath;
            var snapshotOptions = options ?? new SnapshotOptions();

            // Build dependency graph
            var assetFiles = Directory.GetFiles(assetsPath, "*.prefab", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(assetsPath, "*.asset", SearchOption.AllDirectories))
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package"))
                .Take(snapshotOptions.MaxPrefabsToAnalyze > 0 ? snapshotOptions.MaxPrefabsToAnalyze : 100)
                .Select(f => "Assets" + f.Replace(assetsPath, "").Replace('\\', '/'));

            var graph = new Dictionary<string, HashSet<string>>();

            foreach (var asset in assetFiles)
            {
                try
                {
                    var deps = AssetDatabase.GetDependencies(asset, recursive: false);
                    graph[asset] = new HashSet<string>(deps.Where(d => d != asset));
                }
                catch { }
            }

            // Detect cycles using DFS
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();
            var path = new List<string>();

            foreach (var node in graph.Keys)
            {
                if (FindCycleDFS(node, graph, visited, recursionStack, path))
                {
                    if (path.Count > 1)
                    {
                        cycles.Add(new List<string>(path));
                    }
                    path.Clear();
                }
            }

            return cycles;
        }

        private static bool FindCycleDFS(string node, Dictionary<string, HashSet<string>> graph,
            HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
        {
            visited.Add(node);
            recursionStack.Add(node);
            path.Add(node);

            if (graph.ContainsKey(node))
            {
                foreach (var neighbor in graph[node])
                {
                    if (!visited.Contains(neighbor))
                    {
                        if (FindCycleDFS(neighbor, graph, visited, recursionStack, path))
                            return true;
                    }
                    else if (recursionStack.Contains(neighbor))
                    {
                        // Found a cycle
                        path.Add(neighbor);
                        return true;
                    }
                }
            }

            recursionStack.Remove(node);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        /// <summary>
        /// Clears the dependency cache.
        /// </summary>
        public static void ClearCache()
        {
            _dependencyCache = null;
            _lastCacheUpdate = DateTime.MinValue;
        }

        /// <summary>
        /// Gets dependencies for a specific prefab (legacy method).
        /// </summary>
        private static List<(string Type, string Path)> GetPrefabDependencies(string prefabPath, SnapshotOptions options)
        {
            return GetAssetDependencies(prefabPath, options);
        }
    }
}
