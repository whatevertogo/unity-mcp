using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes code structure including namespaces, inheritance relationships, and organization.
    /// Helps AI understand the codebase organization and relationships.
    /// </summary>
    internal static class CodeStructureAnalyzer
    {
        private static Dictionary<string, CodeStructureInfo> _codeStructureCache;
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private const int CACHE_VALIDITY_SECONDS = 120;

        /// <summary>
        /// Information about a code structure element.
        /// </summary>
        public class CodeStructureInfo
        {
            public string Path { get; set; }
            public string Namespace { get; set; }
            public string ClassName { get; set; }
            public string BaseType { get; set; }
            public List<string> Interfaces { get; set; } = new();
            public List<string> Attributes { get; set; } = new();
            public string Category { get; set; }  // MonoBehaviour, ScriptableObject, Plain Class, etc.
            public List<string> References { get; set; } = new();
        }

        /// <summary>
        /// Namespace summary for the snapshot.
        /// </summary>
        public class NamespaceSummary
        {
            public string Name { get; set; }
            public int ClassCount { get; set; }
            public List<string> KeyClasses { get; set; } = new();
        }

        /// <summary>
        /// Analyzes the code structure and returns summaries for the snapshot.
        /// </summary>
        public static Dictionary<string, object> AnalyzeCodeStructure(SnapshotOptions options = null)
        {
            var assetsPath = Application.dataPath;
            var opts = options ?? new SnapshotOptions();

            // Get or scan code structure
            var codeInfo = GetOrScanCodeStructure(assetsPath, opts);

            // Build namespace summaries
            var namespaceSummaries = BuildNamespaceSummaries(codeInfo);

            // Get inheritance hierarchy
            var inheritanceHierarchy = BuildInheritanceHierarchy(codeInfo);

            // Get key components by category
            var componentsByCategory = GroupByCategory(codeInfo);

            return new Dictionary<string, object>
            {
                ["total_classes"] = codeInfo.Count,
                ["namespaces"] = namespaceSummaries.OrderBy(n => n.Name).Select(n => new
                {
                    name = n.Name,
                    count = n.ClassCount,
                    key_classes = n.KeyClasses.Take(5).ToList()
                }).ToList(),
                ["inheritance_tree"] = inheritanceHierarchy,
                ["components_by_category"] = componentsByCategory
            };
        }

        /// <summary>
        /// Gets or scans the code structure with caching.
        /// </summary>
        private static Dictionary<string, CodeStructureInfo> GetOrScanCodeStructure(string assetsPath, SnapshotOptions options)
        {
            if ((DateTime.UtcNow - _lastCacheUpdate).TotalSeconds < CACHE_VALIDITY_SECONDS && _codeStructureCache != null)
            {
                return _codeStructureCache;
            }

            _codeStructureCache = new Dictionary<string, CodeStructureInfo>();
            _lastCacheUpdate = DateTime.UtcNow;

            var scanLimit = options.MaxFilesToScan > 0 ? options.MaxFilesToScan : 100;
            var scriptFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package") && !f.Contains("Packages"))
                .Take(scanLimit);

            foreach (var file in scriptFiles)
            {
                try
                {
                    var info = ParseScriptFile(file, assetsPath);
                    if (info != null && !string.IsNullOrEmpty(info.ClassName))
                    {
                        var key = info.Namespace + "." + info.ClassName;
                        _codeStructureCache[key] = info;
                    }
                }
                catch
                {
                    // Skip files that can't be parsed
                }
            }

            return _codeStructureCache;
        }

        /// <summary>
        /// Parses a C# script file to extract structure information.
        /// </summary>
        private static CodeStructureInfo ParseScriptFile(string filePath, string assetsPath)
        {
            var content = File.ReadAllText(filePath);
            var info = new CodeStructureInfo
            {
                Path = "Assets" + filePath.Replace(assetsPath, "").Replace('\\', '/')
            };

            // Extract namespace
            var namespaceMatch = Regex.Match(content, @"namespace\s+([\w\.]+)");
            if (namespaceMatch.Success)
            {
                info.Namespace = namespaceMatch.Groups[1].Value;
            }
            else
            {
                info.Namespace = "(Global)";
            }

            // Extract class name and base type
            var classPattern = @"(?:public|internal|protected|private|\s)*\s*(?:partial\s+)?class\s+(\w+)(?:\s*:\s*([^{]+))?";
            var structPattern = @"(?:public|internal|protected|private|\s)*\s*struct\s+(\w+)(?:\s*:\s*([^{]+))?";
            var interfacePattern = @"(?:public|internal|protected|private|\s)*\s*interface\s+(\w+)(?:\s*:\s*([^{]+))?";

            Match match = Regex.Match(content, classPattern);
            if (!match.Success) match = Regex.Match(content, structPattern);
            if (!match.Success) match = Regex.Match(content, interfacePattern);

            if (match.Success)
            {
                info.ClassName = match.Groups[1].Value;

                if (match.Groups[2].Success)
                {
                    var baseAndInterfaces = match.Groups[2].Value.Trim();
                    var parts = baseAndInterfaces.Split(',').Select(p => p.Trim()).ToList();

                    if (parts.Count > 0)
                    {
                        // First part is usually the base class
                        var firstPart = parts[0];
                        if (!firstPart.Contains("I") || firstPart.StartsWith("I") && firstPart.Length > 2 && char.IsUpper(firstPart[1]))
                        {
                            // Heuristic: interfaces usually start with I and have another capital
                            info.BaseType = firstPart;
                            if (parts.Count > 1)
                            {
                                info.Interfaces = parts.Skip(1).ToList();
                            }
                        }
                        else
                        {
                            info.Interfaces = parts;
                        }
                    }
                }
            }

            // Determine category
            if (content.Contains(" : MonoBehaviour"))
            {
                info.Category = "MonoBehaviour";
            }
            else if (content.Contains(" : ScriptableObject"))
            {
                info.Category = "ScriptableObject";
            }
            else if (content.Contains(" : EditorWindow") || content.Contains(" : Editor"))
            {
                info.Category = "Editor";
            }
            else if (content.Contains("interface "))
            {
                info.Category = "Interface";
            }
            else if (content.Contains("struct "))
            {
                info.Category = "Struct";
            }
            else if (content.Contains("enum "))
            {
                info.Category = "Enum";
            }
            else
            {
                info.Category = "Class";
            }

            // Extract attributes
            var attributeMatches = Regex.Matches(content, @"\[(\w+)");
            foreach (Match attrMatch in attributeMatches)
            {
                var attr = attrMatch.Groups[1].Value;
                if (!info.Attributes.Contains(attr))
                {
                    info.Attributes.Add(attr);
                }
            }

            return info;
        }

        /// <summary>
        /// Builds namespace summaries from code structure.
        /// </summary>
        private static List<NamespaceSummary> BuildNamespaceSummaries(Dictionary<string, CodeStructureInfo> codeInfo)
        {
            var namespaceGroups = codeInfo.Values
                .GroupBy(c => c.Namespace)
                .Select(g => new NamespaceSummary
                {
                    Name = g.Key,
                    ClassCount = g.Count(),
                    KeyClasses = g.Select(c => c.ClassName).Distinct().Take(10).ToList()
                })
                .ToList();

            return namespaceGroups;
        }

        /// <summary>
        /// Builds inheritance hierarchy for key types.
        /// </summary>
        private static Dictionary<string, object> BuildInheritanceHierarchy(Dictionary<string, CodeStructureInfo> codeInfo)
        {
            var hierarchy = new Dictionary<string, object>();

            // Group by base type
            var byBaseType = codeInfo.Values
                .Where(c => !string.IsNullOrEmpty(c.BaseType))
                .GroupBy(c => c.BaseType)
                .Where(g => g.Count() >= 2) // Only show if multiple inheritors
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(c => c.ClassName).Take(10).ToList()
                );

            hierarchy["common_base_types"] = byBaseType;

            // Count by category
            var byCategory = codeInfo.Values
                .GroupBy(c => c.Category)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count()
                );

            hierarchy["category_counts"] = byCategory;

            return hierarchy;
        }

        /// <summary>
        /// Groups components by category for quick reference.
        /// </summary>
        private static Dictionary<string, List<string>> GroupByCategory(Dictionary<string, CodeStructureInfo> codeInfo)
        {
            return codeInfo.Values
                .GroupBy(c => c.Category)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(c => $"{c.ClassName} ({c.Path})").Take(20).ToList()
                );
        }

        /// <summary>
        /// Gets all classes that inherit from a specific base type.
        /// </summary>
        public static List<string> GetClassesInheritingFrom(string baseTypeName)
        {
            if (_codeStructureCache == null)
            {
                GetOrScanCodeStructure(Application.dataPath, new SnapshotOptions());
            }

            return _codeStructureCache.Values
                .Where(c => c.BaseType == baseTypeName || c.BaseType?.Contains(baseTypeName) == true)
                .Select(c => $"{c.ClassName} ({c.Path})")
                .ToList();
        }

        /// <summary>
        /// Clears the code structure cache.
        /// </summary>
        public static void ClearCache()
        {
            _codeStructureCache = null;
            _lastCacheUpdate = DateTime.MinValue;
        }
    }
}
