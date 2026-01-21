using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Extracts README content to provide ground-truth project description.
    /// Solves the "hallucination" problem - developer's own words > AI inference.
    /// </summary>
    public static class ReadmeExtractor
    {
        private const int MaxPreviewLength = 500;

        /// <summary>
        /// Extracts the first N characters from the project's README.
        /// Searches in project root, then Assets folder.
        /// </summary>
        public static string ExtractPreview()
        {
            // Search locations in order
            var locations = new[]
            {
                "README.md",
                "readme.md",
                "Assets/README.md",
                "Assets/readme.md"
            };

            foreach (var location in locations)
            {
                var fullPath = Path.Combine(Application.dataPath, "..", location);
                if (File.Exists(fullPath))
                {
                    var content = File.ReadAllText(fullPath);
                    return TruncateToSentence(content, MaxPreviewLength);
                }
            }

            return null;
        }

        /// <summary>
        /// Truncates text to the nearest complete sentence within maxLength.
        /// Avoids cutting off mid-sentence.
        /// </summary>
        private static string TruncateToSentence(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Length <= maxLength) return text;

            // Find the last sentence ending within limit
            var truncated = text.Substring(0, maxLength);
            var lastPeriod = truncated.LastIndexOf('.');
            var lastNewline = truncated.LastIndexOf('\n');
            var lastBreak = Math.Max(lastPeriod, lastNewline);

            if (lastBreak > maxLength * 0.7) // Only if we have at least 70% of limit
            {
                return text.Substring(0, lastBreak + 1).Trim() + "...";
            }

            return text.Substring(0, maxLength).Trim() + "...";
        }

        /// <summary>
        /// Checks if a README exists at any standard location.
        /// </summary>
        public static bool ReadmeExists()
        {
            var locations = new[]
            {
                "README.md",
                "readme.md",
                "Assets/README.md"
            };

            foreach (var location in locations)
            {
                var fullPath = Path.Combine(Application.dataPath, "..", location);
                if (File.Exists(fullPath))
                    return true;
            }

            return false;
        }
    }
}
