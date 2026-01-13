using MCPForUnity.Editor.Timeline.Core;

namespace MCPForUnity.Editor.Timeline.Semantics
{
    /// <summary>
    /// Default implementation of event importance scoring.
    /// Scores are based on event type and payload characteristics.
    /// </summary>
    public sealed class DefaultEventScorer : IEventScorer
    {
        /// <summary>
        /// Calculate importance score for an event.
        /// Higher scores indicate more significant events.
        ///
        /// Scoring strategy (L3 Semantic Whitelist):
        /// - Critical (1.0): Build failures, AI notes, critical errors
        /// - High (0.7-0.9): Scripts, Scenes, Component operations, Property changes
        /// - Medium (0.4-0.6): GameObject operations, Asset imports
        /// - Low (0.1-0.3): Hierarchy changes, Play mode toggles
        ///
        /// Default behavior: get_timeline only returns events with score >= 0.4 (medium+)
        /// unless include_low_importance=true is specified.
        /// </summary>
        public float Score(EditorEvent evt)
        {
            return evt.Type switch
            {
                // ========== Critical (1.0) ==========
                // Build failures and AI annotations are top priority
                EventTypes.BuildFailed => 1.0f,
                EventTypes.ScriptCompilationFailed => 1.0f,
                "AINote" => 1.0f,  // AI-written notes are always critical

                // ========== High (0.7-0.9) ==========
                // Scripts and Scenes are project structure changes
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScript(evt) => 1.0f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScene(evt) => 0.9f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsPrefab(evt) => 0.8f,

                // Component and Property modifications (direct user actions)
                EventTypes.ComponentRemoved => 0.7f,
                EventTypes.PropertyModified => 0.6f,  // P0 property-level tracking
                EventTypes.ComponentAdded => 0.6f,

                // Scene operations
                EventTypes.SceneSaved => 0.8f,
                EventTypes.SceneOpened => 0.7f,

                // Build operations (success is important but less than failure)
                EventTypes.BuildStarted => 0.9f,
                EventTypes.BuildCompleted => 1.0f,

                // ========== Medium (0.4-0.6) ==========
                // GameObject operations (structure changes)
                EventTypes.GameObjectDestroyed => 0.6f,
                EventTypes.GameObjectCreated => 0.5f,
                EventTypes.AssetCreated or EventTypes.AssetImported => 0.5f,
                EventTypes.ScriptCompiled => 0.4f,

                // ========== Low (0.1-0.3) ==========
                // Hierarchy changes are noise (happen very frequently)
                EventTypes.HierarchyChanged => 0.2f,
                EventTypes.PlayModeChanged => 0.3f,

                // Default low importance for unknown types
                _ => 0.1f
            };
        }

        private static bool IsScript(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".cs";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Script") == true ||
                       type.ToString()?.Contains("MonoScript") == true;
            return false;
        }

        private static bool IsScene(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".unity";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Scene") == true;
            return false;
        }

        private static bool IsPrefab(EditorEvent e)
        {
            if (e.Payload.TryGetValue("extension", out var ext))
                return ext.ToString() == ".prefab";
            if (e.Payload.TryGetValue("type", out var type))
                return type.ToString()?.Contains("Prefab") == true;
            return false;
        }
    }
}
