using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Shared utility methods for ProjectSnapshot.
    /// Consolidates AssetPathUtils, StringExtensions, and other common utilities.
    /// </summary>
    internal static class Utilities
    {
        #region Asset Path Utilities

        /// <summary>
        /// Gets the asset type from its file path.
        /// This is the single source of truth for type mapping across ProjectSnapshot.
        /// </summary>
        /// <param name="path">The asset file path.</param>
        /// <returns>The asset type string (e.g., "Prefab", "Material", "Texture").</returns>
        public static string GetAssetTypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";

            var ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                // Scenes & Prefabs
                ".prefab" => "Prefab",
                ".unity" => "Scene",

                // Materials & Shaders
                ".mat" => "Material",
                ".shader" => "Shader",

                // 3D Models
                ".fbx" or ".obj" or ".gltf" or ".glb" => "Mesh",

                // Textures
                ".png" or ".jpg" or ".jpeg" or ".tga" or ".psd" or ".tif" or ".bmp" => "Texture",

                // Scripts
                ".cs" => "Script",

                // Animation
                ".controller" => "AnimatorController",
                ".anim" => "AnimationClip",
                ".overrideController" => "AnimatorOverrideController",

                // Asset Files
                ".asset" => "ScriptableObject",

                // UI
                ".guiskin" => "GUISkin",
                ".spriteatlas" => "SpriteAtlas",

                // Physics
                ".physicMaterial" or ".physicsMaterial2D" => "PhysicsMaterial",

                // Rendering
                ".renderTexture" => "RenderTexture",
                ".cube" => "Cubemap",

                // Audio
                ".mixer" => "AudioMixer",

                // Fonts
                ".fontsettings" => "Font",

                // Other Types
                ".mask" => "Mask",
                ".flare" => "LensFlare",
                ".playable" => "PlayableAsset",
                ".tilemap" => "Tilemap",
                ".json" or ".xml" or ".yaml" => "DataFile",

                _ => "Other"
            };
        }

        /// <summary>
        /// Checks if an asset exists at the given path.
        /// Uses AssetDatabase.LoadAssetAtPath for reliable existence check.
        /// </summary>
        /// <param name="assetPath">The asset path to check.</param>
        /// <returns>True if the asset exists, false otherwise.</returns>
        public static bool AssetExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null;
        }

        /// <summary>
        /// Gets the GUID for an asset path.
        /// Returns empty string if asset doesn't exist.
        /// </summary>
        /// <param name="assetPath">The asset path.</param>
        /// <returns>The asset GUID or empty string.</returns>
        public static string GetAssetGuid(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            return AssetDatabase.AssetPathToGUID(assetPath);
        }

        #endregion

        #region String Extensions

        /// <summary>
        /// Determines whether the source string contains the specified value using the specified comparison.
        /// </summary>
        public static bool StringContains(string source, string value, StringComparison comparison)
        {
            return source?.IndexOf(value, comparison) >= 0;
        }

        /// <summary>
        /// Determines whether the source string is in any of the specified values (case-insensitive).
        /// Non-extension method version for when extension method syntax is not desired.
        /// </summary>
        public static bool StringIn(string source, params string[] values)
        {
            return StringInExtension(source, values);
        }

        /// <summary>
        /// Extension method version: Determines whether the source string is in any of the specified values (case-insensitive).
        /// </summary>
        public static bool In(this string source, params string[] values)
        {
            return StringInExtension(source, values);
        }

        private static bool StringInExtension(string source, params string[] values)
        {
            if (source == null || values == null)
                return false;

            foreach (var value in values)
            {
                if (string.Equals(source, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        #region Directory Analysis

        /// <summary>
        /// Gets a descriptive comment for a directory based on its contents.
        /// Shared by DirectoryAnalyzer and ProjectQueryHelpers.
        /// </summary>
        public static string GetDirectoryComment(string dirName, List<string> files, List<string> dirs)
        {
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

        /// <summary>
        /// Gets the file icon emoji for a file based on its extension.
        /// </summary>
        public static string GetFileIcon(string filePath)
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
        /// Determines if a file should be ignored in directory listings.
        /// </summary>
        public static bool ShouldIgnoreFile(string filePath)
        {
            var name = Path.GetFileName(filePath).ToLower();
            return name.StartsWith(".") || name.EndsWith(".meta") ||
                   filePath.Contains("/Library/") || filePath.Contains("/Temp/");
        }

        #endregion
    }
}
