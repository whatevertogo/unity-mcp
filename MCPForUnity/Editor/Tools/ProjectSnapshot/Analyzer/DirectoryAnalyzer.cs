using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Analyzes project directory structure and generates tree view with smart folding.
    /// </summary>
    internal static class DirectoryAnalyzer
    {
        /// <summary>
        /// Folder category for smart folding decisions.
        /// </summary>
        private enum FolderCategory
        {
            Code,           // Code folder - show all files
            Scene,          // Scene folder - show scene files
            ResourceHeavy,  // Resource-heavy folder - collapse to summary
            Config,         // Config folder - show config files
            Mixed           // Mixed folder - show core files
        }

        /// <summary>
        /// Generates the directory map section for the snapshot.
        /// </summary>
        public static void GenerateDirectoryMap(StringBuilder sb, SnapshotOptions options)
        {
            sb.AppendLine("## [SECTION 3: Directory Map]");
            sb.AppendLine();

            int tokenEstimate = 0;
            var maxTokens = options.MaxSnapshotTokens > 0 ? options.MaxSnapshotTokens : 5000;

            if (options.EnableSmartFolding)
            {
                sb.AppendLine("```");
                GenerateDirectoryWithSmartFolding(sb, "Assets", 0, options.MaxDepth, options, ref tokenEstimate, maxTokens);
                sb.AppendLine("```");
            }
            else
            {
                // Fall back to original behavior
                sb.AppendLine("```");
                GenerateDirectoryTree(sb, "Assets", 0, options.MaxDepth, options);
                sb.AppendLine("```");
            }

            sb.AppendLine();
        }

        /// <summary>
        /// Generates directory tree with smart folding logic.
        /// </summary>
        private static void GenerateDirectoryWithSmartFolding(
            StringBuilder sb,
            string rootPath,
            int currentDepth,
            int maxDepth,
            SnapshotOptions options,
            ref int tokenEstimate,
            int maxTokens)
        {
            if (currentDepth > maxDepth) return;
            if (tokenEstimate > maxTokens) return; // Circuit breaker

            var fullPath = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullPath)) return;

            var dirName = new DirectoryInfo(fullPath).Name;

            // Skip Unity's generated folders
            if (!options.IncludePackages &&
                (dirName == "Library" || dirName == "Temp" || dirName == "obj" ||
                 dirName == ".git" || dirName == "Packages" || dirName == "ProjectSettings"))
            {
                return;
            }

            var category = ClassifyFolder(fullPath, dirName);
            var files = Directory.GetFiles(fullPath)
                .Where(f => !ShouldIgnoreFile(f))
                .ToList();
            var subDirs = Directory.GetDirectories(fullPath)
                .Where(d => options.IncludePackages ||
                           !new DirectoryInfo(d).Name.In("Library", "Temp", "obj", ".git"))
                .OrderBy(d => new DirectoryInfo(d).Name)
                .ToList();

            var indent = new string(' ', currentDepth * 2);
            var icon = GetFolderIcon(category);

            switch (category)
            {
                case FolderCategory.ResourceHeavy:
                    // 📂 Art/Textures/ [120 files, 5 subfolders]
                    sb.AppendLine($"{indent}{icon} {dirName}/ [{files.Count} files, {subDirs.Count} subfolders]");
                    tokenEstimate += 20;
                    // Don't recurse into resource-heavy folders
                    break;

                case FolderCategory.Code:
                    // 📂 Scripts/
                    //   📄 PlayerController.cs
                    sb.AppendLine($"{indent}{icon} {dirName}/");
                    tokenEstimate += 10;

                    foreach (var file in files.Where(IsCoreScript))
                    {
                        var fileName = Path.GetFileName(file);
                        var fileIcon = GetFileIcon(file);
                        sb.AppendLine($"{indent}  {fileIcon} {fileName}");
                        tokenEstimate += 15;
                    }

                    var otherScripts = files.Count(f => IsCoreScript(f));
                    if (otherScripts < files.Count && files.Count > 0)
                    {
                        var others = files.Count - otherScripts;
                        if (others > 0)
                        {
                            sb.AppendLine($"{indent}  ... ({others} other files)");
                            tokenEstimate += 10;
                        }
                    }

                    // Recurse into subdirectories
                    foreach (var dir in subDirs)
                    {
                        GenerateDirectoryWithSmartFolding(sb, dir, currentDepth + 1, maxDepth, options, ref tokenEstimate, maxTokens);
                    }
                    break;

                case FolderCategory.Scene:
                    sb.AppendLine($"{indent}{icon} {dirName}/");
                    tokenEstimate += 10;

                    foreach (var file in files.Where(f => f.EndsWith(".unity")))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        sb.AppendLine($"{indent}  🎬 {fileName}");
                        tokenEstimate += 15;
                    }

                    var sceneCount = files.Count(f => f.EndsWith(".unity"));
                    if (sceneCount < files.Count && files.Count > 0)
                    {
                        sb.AppendLine($"{indent}  ... ({files.Count - sceneCount} other files)");
                        tokenEstimate += 10;
                    }

                    // Recurse into subdirectories
                    foreach (var dir in subDirs)
                    {
                        GenerateDirectoryWithSmartFolding(sb, dir, currentDepth + 1, maxDepth, options, ref tokenEstimate, maxTokens);
                    }
                    break;

                case FolderCategory.Config:
                    sb.AppendLine($"{indent}{icon} {dirName}/");
                    tokenEstimate += 10;

                    foreach (var file in files.Where(f => IsConfigFile(f)))
                    {
                        var fileName = Path.GetFileName(file);
                        var fileIcon = GetFileIcon(file);
                        sb.AppendLine($"{indent}  {fileIcon} {fileName}");
                        tokenEstimate += 15;
                    }

                    var configCount = files.Count(f => IsConfigFile(f));
                    if (configCount < files.Count && files.Count > 0)
                    {
                        sb.AppendLine($"{indent}  ... ({files.Count - configCount} other files)");
                        tokenEstimate += 10;
                    }

                    // Recurse into subdirectories
                    foreach (var dir in subDirs)
                    {
                        GenerateDirectoryWithSmartFolding(sb, dir, currentDepth + 1, maxDepth, options, ref tokenEstimate, maxTokens);
                    }
                    break;

                default: // Mixed
                    sb.AppendLine($"{indent}{icon} {dirName}/");
                    tokenEstimate += 10;

                    var shownFiles = 0;
                    foreach (var file in files)
                    {
                        if (ShouldShowFile(file))
                        {
                            var fileName = Path.GetFileName(file);
                            var fileIcon = GetFileIcon(file);
                            sb.AppendLine($"{indent}  {fileIcon} {fileName}");
                            tokenEstimate += 15;
                            shownFiles++;
                        }
                    }

                    if (shownFiles < files.Count && files.Count > 0)
                    {
                        sb.AppendLine($"{indent}  ... ({files.Count - shownFiles} other files)");
                        tokenEstimate += 10;
                    }

                    // Recurse into subdirectories
                    foreach (var dir in subDirs)
                    {
                        GenerateDirectoryWithSmartFolding(sb, dir, currentDepth + 1, maxDepth, options, ref tokenEstimate, maxTokens);
                    }
                    break;
            }
        }

        /// <summary>
        /// Classifies a folder into a category for smart folding.
        /// </summary>
        private static FolderCategory ClassifyFolder(string folderPath, string folderName)
        {
            var nameLower = folderName.ToLower();

            // Resource-heavy folders - trigger smart folding
            if (nameLower.Contains("texture") || nameLower.Contains("material") ||
                nameLower.Contains("audio") || nameLower.Contains("sound") ||
                nameLower.Contains("music") || nameLower.Contains("sfx") ||
                nameLower.Contains("model") || nameLower.Contains("mesh") ||
                nameLower.Contains("animation") || nameLower.Contains("anim") ||
                nameLower.Contains("fx") || nameLower.Contains("effect") ||
                nameLower.Contains("particle") || nameLower.Contains("sprite"))
            {
                return FolderCategory.ResourceHeavy;
            }

            // Code folders - show everything
            if (nameLower == "scripts" || nameLower.Contains("script") ||
                nameLower.Contains("code") || nameLower.Contains("editor"))
            {
                return FolderCategory.Code;
            }

            // Scene folders
            if (nameLower.Contains("scene"))
            {
                return FolderCategory.Scene;
            }

            // Config folders
            if (nameLower.Contains("setting") || nameLower.Contains("config") ||
                nameLower == "resources" || nameLower.Contains("data"))
            {
                return FolderCategory.Config;
            }

            return FolderCategory.Mixed;
        }

        /// <summary>
        /// Gets the emoji icon for a folder category.
        /// </summary>
        private static string GetFolderIcon(FolderCategory category)
        {
            return category switch
            {
                FolderCategory.Code => "📂",
                FolderCategory.Scene => "🎬",
                FolderCategory.ResourceHeavy => "📦",
                FolderCategory.Config => "⚙️",
                FolderCategory.Mixed => "📂",
                _ => "📂"
            };
        }

        /// <summary>
        /// Gets the emoji icon for a file based on its extension.
        /// </summary>
        private static string GetFileIcon(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".cs" => "📄",
                ".unity" => "🎬",
                ".prefab" => "🧩",
                ".asset" => "💾",
                ".json" => "📋",
                ".xml" => "📋",
                ".yaml" => "📋",
                ".mat" => "🎨",
                ".guiskin" => "👔",
                ".spriteatlas" => "🖼️",
                ".renderTexture" => "🖼️",
                ".shader" => "🎨",
                _ => "📄"
            };
        }

        /// <summary>
        /// Determines if a file is a core script file.
        /// </summary>
        private static bool IsCoreScript(string filePath)
        {
            return filePath.EndsWith(".cs") ||
                   filePath.EndsWith(".asmdef") ||
                   filePath.EndsWith(".asmref");
        }

        /// <summary>
        /// Determines if a file is a config file.
        /// </summary>
        private static bool IsConfigFile(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return ext == ".json" || ext == ".xml" || ext == ".yaml" ||
                   ext == ".asset" || ext == ".bytes";
        }

        /// <summary>
        /// Determines if a file should be shown in the directory tree.
        /// </summary>
        private static bool ShouldShowFile(string filePath)
        {
            return IsCoreScript(filePath) ||
                   filePath.EndsWith(".prefab") ||
                   filePath.EndsWith(".asset") ||
                   filePath.EndsWith(".unity") ||
                   filePath.EndsWith(".json") ||
                   filePath.EndsWith(".asmdef");
        }

        /// <summary>
        /// Determines if a file should be ignored.
        /// </summary>
        private static bool ShouldIgnoreFile(string filePath)
        {
            var name = Path.GetFileName(filePath).ToLower();
            return name.StartsWith(".") || name.EndsWith(".meta") ||
                   filePath.Contains("/Library/") || filePath.Contains("/Temp/");
        }

        /// <summary>
        /// Recursively generates directory tree (legacy method for non-smart-folding mode).
        /// </summary>
        private static void GenerateDirectoryTree(StringBuilder sb, string rootPath, int currentDepth,
            int maxDepth, SnapshotOptions options)
        {
            if (currentDepth > maxDepth) return;

            var fullPath = Path.GetFullPath(rootPath);
            if (!Directory.Exists(fullPath)) return;

            // Skip Unity's generated folders
            var dirName = new DirectoryInfo(fullPath).Name;
            if (!options.IncludePackages &&
                (dirName == "Library" || dirName == "Temp" || dirName == "obj" ||
                 dirName == ".git" || dirName == "Packages" || dirName == "ProjectSettings"))
            {
                return;
            }

            // Get directories (sorted)
            var dirs = Directory.GetDirectories(fullPath)
                .Where(d => options.IncludePackages ||
                           !new DirectoryInfo(d).Name.In("Library", "Temp", "obj", ".git"))
                .OrderBy(d => new DirectoryInfo(d).Name)
                .ToList();

            // Get files (only important ones)
            var files = Directory.GetFiles(fullPath)
                .Where(f => !new FileInfo(f).Name.StartsWith(".") &&
                           !f.Contains(".meta") &&
                           !f.Contains(".cs"))
                .OrderBy(f => new FileInfo(f).Name)
                .ToList();

            // Add directory comment for key folders
            var comment = GetDirectoryComment(dirName, files, dirs);
            if (!string.IsNullOrEmpty(comment))
            {
                sb.AppendLine($"{new string(' ', currentDepth * 2)}{dirName}/ # {comment}");
            }
            else
            {
                sb.AppendLine($"{new string(' ', currentDepth * 2)}{dirName}/");
            }

            // Recurse into subdirectories
            foreach (var dir in dirs)
            {
                GenerateDirectoryTree(sb, dir, currentDepth + 1, maxDepth, options);
            }
        }

        /// <summary>
        /// Gets a descriptive comment for a directory based on its contents.
        /// </summary>
        private static string GetDirectoryComment(string dirName, List<string> files, List<string> dirs)
        {
            // Detect and comment on important folders
            if (dirName == "Prefabs") return "预制体库";
            if (dirName == "Scripts") return "C# 脚本";
            if (dirName == "Scenes") return $"场景文件 ({files.Count(f => f.EndsWith(".unity"))} scenes)";
            if (dirName == "Resources") return "运行时动态加载资源";
            if (dirName == "Textures") return $"纹理 ({files.Count} files)";
            if (dirName == "Materials") return $"材质球 ({files.Count} files)";
            if (dirName == "Models") return $"3D 模型 ({files.Count} files)";
            if (dirName == "Audio") return $"音频资源 ({files.Count} files)";
            if (dirName == "Animations") return $"动画片段 ({files.Count} files)";
            if (dirName == "ScriptableObjects") return "数据容器";
            if (dirName == "Data") return "数据文件 (JSON/ScriptableObject)";
            if (dirName == "Editor") return "编辑器扩展脚本";

            // Detect content-based comments
            if (dirs.Any(d => new DirectoryInfo(d).Name == "Items") ||
                files.Any(f => f.Contains("Item")))
            {
                return "可能包含道具相关内容";
            }
            if (dirs.Any(d => new DirectoryInfo(d).Name == "Player") ||
                files.Any(f => f.Contains("Player")))
            {
                return "可能包含玩家相关内容";
            }

            return null;
        }
    }
}
