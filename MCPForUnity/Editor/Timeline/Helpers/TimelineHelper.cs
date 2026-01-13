using System;

namespace MCPForUnity.Editor.Timeline.Helpers
{
    /// <summary>
    /// Helper utilities for Timeline feature.
    ///
    /// Centralized common formatting and conversion methods
    /// to avoid code duplication across Timeline components.
    /// </summary>
    public static class TimelineHelper
    {
        /// <summary>
        /// Formats a tool name for display.
        /// Converts snake_case to Title Case.
        ///
        /// Examples:
        /// - "manage_gameobject" → "Manage GameObject"
        /// - "add_timeline_note" → "Add Timeline Note"
        /// - "get_timeline" → "Get Timeline"
        ///
        /// Used in:
        /// - TransactionAggregator (summary generation)
        /// - UndoGroupManager (Undo group names)
        /// </summary>
        public static string FormatToolName(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return "AI Operation";

            // Simple snake_case to Title Case conversion
            // Matches underscore + lowercase letter, replaces with uppercase letter
            return System.Text.RegularExpressions.Regex.Replace(
                toolName,
                "_([a-z])",
                match => match.Groups[1].Value.ToUpper()
            );
        }

        /// <summary>
        /// Formats duration for display.
        /// Converts milliseconds to human-readable "X.Xs" format.
        ///
        /// Examples:
        /// - 500 → "0.5s"
        /// - 1500 → "1.5s"
        /// - 2340 → "2.3s"
        ///
        /// Used in:
        /// - TransactionAggregator (AtomicOperation.DurationMs display)
        /// </summary>
        public static string FormatDuration(long milliseconds)
        {
            return $"{milliseconds / 1000.0:F1}s";
        }

        /// <summary>
        /// Formats duration from a timestamp range.
        ///
        /// Parameters:
        ///   startMs: Start timestamp in milliseconds
        ///   endMs: End timestamp in milliseconds
        ///
        /// Returns:
        ///   Human-readable duration string (e.g., "2.3s")
        /// </summary>
        public static string FormatDurationFromRange(long startMs, long endMs)
        {
            return FormatDuration(endMs - startMs);
        }
    }
}
