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
        /// </summary>
        public float Score(EditorEvent evt)
        {
            return evt.Type switch
            {
                // Asset creation is high importance, especially scripts and scenes
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScript(evt) => 1.0f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsScene(evt) => 0.9f,
                EventTypes.AssetCreated or EventTypes.AssetImported when IsPrefab(evt) => 0.8f,
                EventTypes.AssetCreated or EventTypes.AssetImported => 0.5f,

                // GameObject operations
                EventTypes.GameObjectCreated => 0.5f,
                EventTypes.GameObjectDestroyed => 0.6f,

                // Component operations
                EventTypes.ComponentAdded => 0.6f,
                EventTypes.ComponentRemoved => 0.7f,

                // Scene operations
                EventTypes.SceneSaved => 0.8f,
                EventTypes.SceneOpened => 0.7f,

                // Hierarchy changes are less significant (happen frequently)
                EventTypes.HierarchyChanged => 0.2f,

                // Script operations
                EventTypes.ScriptCompiled => 0.4f,
                EventTypes.ScriptCompilationFailed => 0.9f,

                // Build operations
                EventTypes.BuildStarted => 0.9f,
                EventTypes.BuildCompleted => 1.0f,
                EventTypes.BuildFailed => 1.0f,

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
