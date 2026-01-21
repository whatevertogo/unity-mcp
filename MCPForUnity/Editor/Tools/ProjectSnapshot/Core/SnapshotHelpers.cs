using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    /// <summary>
    /// Shared helper methods for ProjectSnapshot tools.
    /// Reduces code duplication and provides consistent behavior.
    /// Pattern follows GameObjectComponentHelpers and QueryHelpers.
    /// </summary>
    internal static class SnapshotHelpers
    {
        /// <summary>
        /// Gets settings and options with null-safety.
        /// Pattern borrowed from GameObjectComponentHelpers.
        /// </summary>
        internal static (ProjectSnapshotSettings settings, SnapshotOptions options) GetSettingsAndOptions()
        {
            var settings = ProjectSnapshotSettings.Instance;
            var options = settings?.ToOptions() ?? new SnapshotOptions();
            return (settings, options);
        }

        /// <summary>
        /// Builds a status response dictionary from SnapshotStatus.
        /// Provides consistent field naming across tools.
        /// </summary>
        internal static Dictionary<string, object> BuildStatusResponse(SnapshotStatus status)
        {
            return new Dictionary<string, object>
            {
                ["exists"] = status.Exists,
                ["dependencies_exist"] = status.DependenciesExist,
                ["last_generated"] = status.LastGenerated?.ToString("o"),
                ["age_minutes"] = Math.Round(status.AgeMinutes, 1),
                ["is_dirty"] = status.IsDirty,
                ["recommendation"] = status.Recommendation ??
                    (status.IsDirty ? "Consider regenerating" : "Snapshot is up to date")
            };
        }

        /// <summary>
        /// Reads snapshot content with BOM handling.
        /// UTF-8 BOM is stripped if present for cleaner output.
        /// </summary>
        internal static string ReadSnapshotContentSafe(string path)
        {
            var content = ProjectSnapshotGenerator.ReadSnapshotContent(path);
            if (content == null)
                return null;

            // Remove UTF-8 BOM if present
            if (content.Length > 0 && content[0] == '\uFEFF')
                return content.Substring(1);

            return content;
        }

        /// <summary>
        /// Creates an error response for missing snapshot.
        /// Provides consistent messaging across tools.
        /// </summary>
        internal static object CreateSnapshotNotFoundResponse(string expectedPath)
        {
            return new ErrorResponse(
                "No snapshot found yet. It will be auto-generated after script compilation. " +
                $"Expected location: {expectedPath}"
            );
        }
    }
}
