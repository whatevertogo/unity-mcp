using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes project architecture patterns and loading strategies.
    /// Enhanced with more pattern recognition capabilities for better AI understanding.
    /// </summary>
    internal static class ArchitectureAnalyzer
    {
        // Cache for detected patterns to avoid repeated file scans
        private static Dictionary<string, List<string>> _patternCache = new Dictionary<string, List<string>>();
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private const int CACHE_VALIDITY_SECONDS = 60;

        /// <summary>
        /// Detects the architecture type of the project with enhanced pattern recognition.
        /// </summary>
        public static ArchitectureType DetectArchitectureType()
        {
            var assetsPath = Application.dataPath;
            var indicators = new List<string>();
            var patterns = GetOrScanPatterns(assetsPath);

            // Check for data-driven indicators
            if (patterns.ContainsKey("json_files") && patterns["json_files"].Count > 0)
            {
                indicators.Add($"JSON data files ({patterns["json_files"].Count})");
            }

            if (patterns.ContainsKey("scriptable_objects") && patterns["scriptable_objects"].Count > 0)
            {
                indicators.Add($"ScriptableObject types ({patterns["scriptable_objects"].Count})");
            }

            // Check for architectural patterns
            if (patterns.ContainsKey("singletons") && patterns["singletons"].Count > 0)
            {
                indicators.Add($"Singleton pattern ({patterns["singletons"].Count})");
            }

            if (patterns.ContainsKey("events") && patterns["events"].Count > 0)
            {
                indicators.Add($"Event/Delegate system ({patterns["events"].Count})");
            }

            if (patterns.ContainsKey("state_machines") && patterns["state_machines"].Count > 0)
            {
                indicators.Add($"State pattern ({patterns["state_machines"].Count})");
            }

            if (patterns.ContainsKey("dependency_injection") && patterns["dependency_injection"].Count > 0)
            {
                indicators.Add("DI/Service Locator pattern");
            }

            // Check for dynamic loading indicators
            if (patterns.ContainsKey("addressables") && patterns["addressables"].Count > 0)
            {
                indicators.Add("Addressables system");
            }

            if (patterns.ContainsKey("resources_folders") && patterns["resources_folders"].Count > 0)
            {
                indicators.Add($"Resources folders ({patterns["resources_folders"].Count})");
            }

            if (patterns.ContainsKey("asset_bundles") && patterns["asset_bundles"].Count > 0)
            {
                indicators.Add("AssetBundle system");
            }

            // Detect project scale
            var scriptCount = patterns.ContainsKey("all_scripts") ? patterns["all_scripts"].Count : 0;
            var scaleIndicator = scriptCount switch
            {
                < 20 => "Small",
                < 100 => "Medium",
                < 500 => "Large",
                _ => "Very Large"
            };

            // Determine architecture type
            string archType;
            string confidence;

            if (indicators.Count == 0)
            {
                archType = "Standard / Static";
                confidence = $"No special patterns detected. Scale: {scaleIndicator} ({scriptCount} scripts)";
            }
            else if (patterns.ContainsKey("addressables") && patterns["addressables"].Count > 0)
            {
                archType = "Addressables-Based / Async Loading";
                confidence = $"Primary: Addressables. Also: {string.Join(", ", indicators.Where(i => !i.Contains("Addressables")).Take(2))}";
            }
            else if (patterns.ContainsKey("dependency_injection") && patterns["dependency_injection"].Count > 0)
            {
                archType = "Service-Oriented / Decoupled";
                confidence = $"Primary: DI/Service Locator. Scale: {scaleIndicator}";
            }
            else if (indicators.Count >= 3)
            {
                archType = "Data-Driven / Dynamic Loading";
                confidence = $"Multiple patterns: {string.Join(", ", indicators.Take(3))}";
            }
            else
            {
                archType = "Semi-Data-Driven / Mixed";
                confidence = $"Patterns: {string.Join(", ", indicators)}";
            }

            return new ArchitectureType
            {
                Type = archType,
                Confidence = confidence
            };
        }

        /// <summary>
        /// Gets cached patterns or scans for them if cache is stale.
        /// </summary>
        private static Dictionary<string, List<string>> GetOrScanPatterns(string assetsPath)
        {
            if ((DateTime.UtcNow - _lastCacheUpdate).TotalSeconds < CACHE_VALIDITY_SECONDS && _patternCache.Count > 0)
            {
                return _patternCache;
            }

            _patternCache = new Dictionary<string, List<string>>();
            _lastCacheUpdate = DateTime.UtcNow;

            try
            {
                // Scan for JSON files
                _patternCache["json_files"] = Directory.GetFiles(assetsPath, "*.json", SearchOption.AllDirectories)
                    .Where(x => !x.Contains("Package") && !x.Contains("Library") && !x.Contains("Temp") && !x.Contains("Packages"))
                    .ToList();

                // Scan for all C# scripts
                var allScripts = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                    .Where(x => !x.Contains("Library") && !x.Contains("Temp") && !x.Contains("Package") && !x.Contains("Packages"))
                    .ToList();
                _patternCache["all_scripts"] = allScripts;

                // Scan for ScriptableObjects
                _patternCache["scriptable_objects"] = allScripts
                    .Where(f => File.ReadAllText(f).Contains("ScriptableObject"))
                    .ToList();

                // Scan for singletons (common patterns)
                _patternCache["singletons"] = allScripts
                    .Where(f =>
                    {
                        var content = File.ReadAllText(f);
                        return content.Contains("static") && content.Contains("Instance") &&
                               (content.Contains("MonoBehaviour") || content.Contains("ScriptableObject"));
                    })
                    .ToList();

                // Scan for event/delegate systems
                _patternCache["events"] = allScripts
                    .Where(f =>
                    {
                        var content = File.ReadAllText(f);
                        return content.Contains("event ") || content.Contains("UnityEvent") ||
                               content.Contains("Action<") || content.Contains("delegate ");
                    })
                    .ToList();

                // Scan for state machines
                _patternCache["state_machines"] = allScripts
                    .Where(f =>
                    {
                        var content = File.ReadAllText(f);
                        return content.Contains("State") && (content.Contains("Enter") || content.Contains("Exit")) &&
                               (content.Contains("Transition") || content.Contains("ChangeState"));
                    })
                    .ToList();

                // Scan for DI/Service Locator
                _patternCache["dependency_injection"] = allScripts
                    .Where(f =>
                    {
                        var content = File.ReadAllText(f);
                        return content.Contains("ServiceLocator") || content.Contains("DependencyContainer") ||
                               content.Contains("Inject(") || content.Contains("[Inject]");
                    })
                    .ToList();

                // Scan for Addressables
                _patternCache["addressables"] = allScripts
                    .Where(f => File.ReadAllText(f).Contains("Addressables"))
                    .ToList();

                // Scan for Resources folders
                _patternCache["resources_folders"] = Directory.GetDirectories(assetsPath, "Resources", SearchOption.AllDirectories)
                    .Where(x => !x.Contains("Library"))
                    .ToList();

                // Scan for AssetBundle references
                _patternCache["asset_bundles"] = allScripts
                    .Where(f => File.ReadAllText(f).Contains("AssetBundle"))
                    .ToList();

                // Scan for scene references
                _patternCache["scene_files"] = Directory.GetFiles(assetsPath, "*.unity", SearchOption.AllDirectories)
                    .Where(x => !x.Contains("Library"))
                    .ToList();
            }
            catch
            {
                // If scanning fails, return empty cache
            }

            return _patternCache;
        }

        /// <summary>
        /// Finds entry point scripts in the project with enhanced detection.
        /// </summary>
        public static List<EntryPoint> FindEntryPoints(SnapshotOptions options = null)
        {
            var entryPoints = new List<EntryPoint>();
            var assetsPath = Application.dataPath;
            var patterns = GetOrScanPatterns(assetsPath);

            // Get patterns from options; skip if not configured
            var snapshotOptions = options ?? new SnapshotOptions();
            var userPatterns = snapshotOptions.Patterns?.EntryPointPatterns;
            var hasUserPatterns = userPatterns != null && userPatterns.Count > 0;

            // Get exclude paths - default to common test/editor paths
            var excludePaths = snapshotOptions.Patterns?.EntryPointExcludePaths
                ?? new[] { "Tests", "Editor", "Plugins", "ThirdParty" };

            // Use default patterns if none configured
            var searchPatterns = hasUserPatterns ? userPatterns : GetDefaultEntryPointPatterns();

            // Helper to check if a path should be excluded
            bool ShouldExcludePath(string relativePath)
            {
                foreach (var exclude in excludePaths)
                {
                    if (relativePath.Contains(exclude, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            // First, scan for explicit entry point patterns
            foreach (var file in patterns.ContainsKey("all_scripts") ? patterns["all_scripts"] : new List<string>())
            {
                var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');

                // Skip if in excluded path
                if (ShouldExcludePath(relativePath))
                    continue;

                var filename = Path.GetFileNameWithoutExtension(file);
                foreach (var pattern in searchPatterns)
                {
                    if (filename.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        entryPoints.Add(new EntryPoint
                        {
                            Path = relativePath,
                            Reason = pattern.Value
                        });
                        break;
                    }
                }
            }

            // Auto-detect Bootstrap/RuntimeInitializeOnLoadMethod
            if (!hasUserPatterns || entryPoints.Count < 5)
            {
                foreach (var file in patterns.ContainsKey("all_scripts") ? patterns["all_scripts"] : new List<string>())
                {
                    try
                    {
                        var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');

                        // Skip if in excluded path
                        if (ShouldExcludePath(relativePath))
                            continue;

                        var content = File.ReadAllText(file);

                        // Check for RuntimeInitializeOnLoadMethod
                        if (content.Contains("[RuntimeInitializeOnLoadMethod"))
                        {
                            if (!entryPoints.Any(ep => ep.Path == relativePath))
                            {
                                entryPoints.Add(new EntryPoint
                                {
                                    Path = relativePath,
                                    Reason = "Auto-start via RuntimeInitializeOnLoadMethod"
                                });
                            }
                        }

                        // Check for scenes in Build Settings (marked as entry point)
                        if (file.EndsWith(".cs"))
                        {
                            var className = Path.GetFileNameWithoutExtension(file);
                            // Common initialization patterns
                            if (className.Equals("Bootstrap", StringComparison.OrdinalIgnoreCase) ||
                                className.Equals("Initializer", StringComparison.OrdinalIgnoreCase) ||
                                className.Equals("Application", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!entryPoints.Any(ep => ep.Path == relativePath))
                                {
                                    entryPoints.Add(new EntryPoint
                                    {
                                        Path = relativePath,
                                        Reason = "Auto-detected initialization class"
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return entryPoints;
        }

        /// <summary>
        /// Gets default entry point patterns when none are configured.
        /// </summary>
        private static Dictionary<string, string> GetDefaultEntryPointPatterns()
        {
            return new Dictionary<string, string>
            {
                { "SceneLoader", "Scene loading/management" },
                { "LevelManager", "Level management" },
                { "GameManager", "Game state management" },
                { "GameController", "Main game controller" },
                { "Bootstrap", "Initialization entry point" },
                { "Main", "Main entry point" },
                { "EntryPoint", "Explicit entry point" },
                { "ApplicationInitializer", "App initialization" },
                { "Startup", "Startup sequence" },
                { "Core", "Core system" }
            };
        }

        /// <summary>
        /// Detects the asset loading strategy used by the project with enhanced detection.
        /// </summary>
        public static LoadingStrategy DetectLoadingStrategy()
        {
            var indicators = new List<string>();
            var assetsPath = Application.dataPath;
            var patterns = GetOrScanPatterns(assetsPath);

            // Check Addressables
            if (patterns.ContainsKey("addressables") && patterns["addressables"].Count > 0)
            {
                return new LoadingStrategy
                {
                    Method = "Addressables (Async)",
                    Indicators = new List<string>
                    {
                        $"Addressables references in {patterns["addressables"].Count} files",
                        "Supports async loading and content delivery"
                    }
                };
            }

            // Check AssetBundle
            if (patterns.ContainsKey("asset_bundles") && patterns["asset_bundles"].Count > 0)
            {
                return new LoadingStrategy
                {
                    Method = "AssetBundle",
                    Indicators = new List<string>
                    {
                        $"AssetBundle references in {patterns["asset_bundles"].Count} files",
                        "Runtime asset loading from bundles"
                    }
                };
            }

            // Check Resources
            if (patterns.ContainsKey("resources_folders") && patterns["resources_folders"].Count > 0)
            {
                var resInfo = new List<string>();
                foreach (var folder in patterns["resources_folders"])
                {
                    var folderName = new DirectoryInfo(folder).Name;
                    var parentFolder = Directory.GetParent(folder)?.Name;
                    resInfo.Add($"{parentFolder}/{folderName}");
                }

                var indicatorsList = new List<string>
                {
                    $"Resources folders: {patterns["resources_folders"].Count}"
                };
                indicatorsList.AddRange(resInfo.Take(3));

                return new LoadingStrategy
                {
                    Method = "Resources.Load",
                    Indicators = indicatorsList
                };
            }

            // Check for direct scene loading
            if (patterns.ContainsKey("scene_files") && patterns["scene_files"].Count > 0)
            {
                return new LoadingStrategy
                {
                    Method = "Scene-Based / Direct References",
                    Indicators = new List<string>
                    {
                        $"{patterns["scene_files"].Count} scenes found",
                        "Assets referenced directly in scenes"
                    }
                };
            }

            return new LoadingStrategy
            {
                Method = "Direct References / Embedded",
                Indicators = new List<string> { "No dynamic loading detected" }
            };
        }

        /// <summary>
        /// Finds all manager classes in the project with enhanced detection.
        /// </summary>
        public static List<string> FindManagerClasses(SnapshotOptions options = null)
        {
            var managers = new List<string>();
            var assetsPath = Application.dataPath;
            var patterns = GetOrScanPatterns(assetsPath);

            // Get patterns from options
            var snapshotOptions = options ?? new SnapshotOptions();
            var userPatterns = snapshotOptions.Patterns?.ManagerClassPatterns;

            // Use default patterns if none configured
            var managerPatterns = userPatterns?.Length > 0 ? userPatterns : new[] { "Manager", "Controller", "Service", "System", "Handler", "Registry", "Broker" };
            var excludePrefixes = snapshotOptions.Patterns?.ManagerExcludePrefixes ?? new[] { "Unity", "Editor", "Test" };

            // Scan scripts for manager patterns
            foreach (var file in patterns.ContainsKey("all_scripts") ? patterns["all_scripts"] : new List<string>())
            {
                var filename = Path.GetFileNameWithoutExtension(file);

                // Check if filename matches any manager pattern
                bool matchesPattern = managerPatterns.Any(p => filename.Contains(p, StringComparison.OrdinalIgnoreCase));

                // Check if filename should be excluded
                bool shouldExclude = excludePrefixes.Any(prefix => filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (matchesPattern && !shouldExclude)
                {
                    var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');
                    managers.Add(relativePath);
                }
            }

            // Also add singletons as they are typically managers
            if (patterns.ContainsKey("singletons"))
            {
                foreach (var file in patterns["singletons"])
                {
                    var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');
                    if (!managers.Contains(relativePath))
                    {
                        managers.Add(relativePath);
                    }
                }
            }

            // Apply limit if configured (0 = unlimited)
            var limit = snapshotOptions.MaxManagerClasses;
            if (limit > 0)
            {
                managers = managers.Take(limit).ToList();
            }

            return managers;
        }

        /// <summary>
        /// Detects key architectural patterns in the project.
        /// </summary>
        public static Dictionary<string, int> DetectArchitecturalPatterns()
        {
            var assetsPath = Application.dataPath;
            var patterns = GetOrScanPatterns(assetsPath);
            var result = new Dictionary<string, int>();

            if (patterns.ContainsKey("singletons")) result["Singletons"] = patterns["singletons"].Count;
            if (patterns.ContainsKey("events")) result["Event/Delegates"] = patterns["events"].Count;
            if (patterns.ContainsKey("state_machines")) result["State Machines"] = patterns["state_machines"].Count;
            if (patterns.ContainsKey("dependency_injection")) result["DI Components"] = patterns["dependency_injection"].Count;
            if (patterns.ContainsKey("scriptable_objects")) result["ScriptableObjects"] = patterns["scriptable_objects"].Count;

            return result;
        }

        /// <summary>
        /// Gets project statistics for the snapshot.
        /// </summary>
        public static Dictionary<string, object> GetProjectStatistics()
        {
            var assetsPath = Application.dataPath;
            var patterns = GetOrScanPatterns(assetsPath);

            var stats = new Dictionary<string, object>
            {
                ["total_scripts"] = patterns.ContainsKey("all_scripts") ? patterns["all_scripts"].Count : 0,
                ["scriptable_objects"] = patterns.ContainsKey("scriptable_objects") ? patterns["scriptable_objects"].Count : 0,
                ["json_files"] = patterns.ContainsKey("json_files") ? patterns["json_files"].Count : 0,
                ["scenes"] = patterns.ContainsKey("scene_files") ? patterns["scene_files"].Count : 0,
                ["resources_folders"] = patterns.ContainsKey("resources_folders") ? patterns["resources_folders"].Count : 0
            };

            return stats;
        }
    }
}
