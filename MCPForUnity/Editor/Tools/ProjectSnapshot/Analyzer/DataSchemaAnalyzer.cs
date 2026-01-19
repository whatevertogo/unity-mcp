using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes data schemas (ScriptableObjects and JSON files).
    /// </summary>
    internal static class DataSchemaAnalyzer
    {
        /// <summary>
        /// Generates the data schema section for the snapshot.
        /// </summary>
        public static void GenerateDataSchema(StringBuilder sb, SnapshotOptions options = null)
        {
            var snapshotOptions = options ?? new SnapshotOptions();
            sb.AppendLine("## [SECTION 5: Data Schema]");
            sb.AppendLine();

            // Find ScriptableObject data definitions
            var scriptableObjects = FindScriptableObjectDataTypes(snapshotOptions);
            if (scriptableObjects.Count > 0)
            {
                sb.AppendLine("### ScriptableObject Data Types");
                sb.AppendLine();
                foreach (var so in scriptableObjects)
                {
                    sb.AppendLine($"#### `{so.ClassName}`");
                    sb.AppendLine($"```csharp");
                    sb.AppendLine($"// Path: {so.Path}");
                    sb.AppendLine(so.CodeSnippet);
                    sb.AppendLine($"```");
                    sb.AppendLine();
                }
            }

            // Find JSON data examples
            var jsonFiles = FindJsonDataFiles(snapshotOptions);
            if (jsonFiles.Count > 0)
            {
                sb.AppendLine("### JSON Data Examples");
                sb.AppendLine();
                foreach (var json in jsonFiles)
                {
                    sb.AppendLine($"#### `{json.Path}`");
                    sb.AppendLine($"```json");
                    sb.AppendLine(json.Content);
                    sb.AppendLine($"```");
                    sb.AppendLine();
                }
            }
        }

        /// <summary>
        /// Finds ScriptableObject data types in the project.
        /// </summary>
        public static List<ScriptableObjectInfo> FindScriptableObjectDataTypes(SnapshotOptions options)
        {
            var result = new List<ScriptableObjectInfo>();
            var assetsPath = Application.dataPath;

            // Find ScriptableObject classes
            var csQuery = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package") &&
                           !f.Contains("Editor"));

            // Apply scan limit if configured (0 = unlimited)
            var scanLimit = options.MaxFilesToScan;
            if (scanLimit > 0)
            {
                csQuery = csQuery.Take(scanLimit);
            }

            foreach (var file in csQuery)
            {
                var content = File.ReadAllText(file);
                if (content.Contains("ScriptableObject"))
                {
                    var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');
                    var className = Path.GetFileNameWithoutExtension(file);
                    var snippet = ExtractClassSnippet(content, className);

                    result.Add(new ScriptableObjectInfo
                    {
                        Path = relativePath,
                        ClassName = className,
                        CodeSnippet = snippet
                    });
                }
            }

            // Apply output limit if configured (0 = unlimited)
            var outputLimit = options.MaxScriptableObjects;
            if (outputLimit > 0)
            {
                result = result.Take(outputLimit).ToList();
            }

            return result;
        }

        /// <summary>
        /// Finds JSON data files in the project.
        /// </summary>
        public static List<JsonFileInfo> FindJsonDataFiles(SnapshotOptions options)
        {
            var result = new List<JsonFileInfo>();
            var assetsPath = Application.dataPath;

            var jsonQuery = Directory.GetFiles(assetsPath, "*.json", SearchOption.AllDirectories)
                .Where(f => !f.Contains("Library") && !f.Contains("Temp") && !f.Contains("Package") &&
                           !f.Contains("Packages"));

            // Apply limit if configured (0 = unlimited)
            var limit = options.MaxJsonExamples;
            if (limit > 0)
            {
                jsonQuery = jsonQuery.Take(limit);
            }

            foreach (var file in jsonQuery)
            {
                var relativePath = "Assets" + file.Replace(assetsPath, "").Replace('\\', '/');
                var content = File.ReadAllText(file);

                // Truncate if too large
                if (content.Length > 2000)
                {
                    content = content.Substring(0, 2000) + "\n... (truncated)";
                }

                result.Add(new JsonFileInfo
                {
                    Path = relativePath,
                    Content = content
                });
            }

            return result;
        }

        /// <summary>
        /// Extracts a code snippet for a class definition.
        /// </summary>
        private static string ExtractClassSnippet(string content, string className)
        {
            // Try to extract the class definition with fields
            var lines = content.Split('\n');
            var inClass = false;
            var snippet = new List<string>();
            var braceCount = 0;
            var capturedLines = 0;

            foreach (var line in lines)
            {
                if (line.Contains($"class {className}") ||
                    line.Contains($"class {className.Split('.')[0]}"))
                {
                    inClass = true;
                }

                if (inClass)
                {
                    snippet.Add(line.Trim());
                    braceCount += line.Count(c => c == '{') - line.Count(c => c == '}');
                    capturedLines++;

                    if (braceCount == 0 && capturedLines > 1)
                    {
                        break;
                    }

                    if (capturedLines > 20) // Limit snippet size
                    {
                        snippet.Add("    ...");
                        break;
                    }
                }
            }

            return string.Join("\n", snippet);
        }
    }
}
