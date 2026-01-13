using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Timeline.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MCPForUnity.Editor.Timeline.Capture
{
    /// <summary>
    /// Captures Unity editor events and records them to the EventStore.
    /// Uses debouncing to avoid spamming for rapid successive changes.
    /// Updated to use EventType constants for type safety.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityEventHooks
    {
        private static DateTime _lastHierarchyChange;
        private static readonly object _lock = new();

        static UnityEventHooks()
        {
            // Monitor GameObject/component creation
            ObjectFactory.componentWasAdded += OnComponentAdded;

            // Monitor hierarchy changes (with debouncing)
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            // Monitor play mode state changes
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Monitor scene saving - use EditorSceneManager, not EditorApplication
            EditorSceneManager.sceneSaving += OnSceneSaving;

            // Monitor scene opening - use EditorSceneManager, not EditorApplication
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component == null) return;

            var payload = new Dictionary<string, object>
            {
                ["component_type"] = component.GetType().Name,
                ["game_object"] = component.gameObject?.name ?? "Unknown"
            };

            RecordEvent(EventTypes.ComponentAdded, component.GetInstanceID().ToString(), payload);
        }

        private static void OnHierarchyChanged()
        {
            var now = DateTime.Now;
            lock (_lock)
            {
                // Debounce: ignore changes within 200ms of the last one
                if ((now - _lastHierarchyChange).TotalMilliseconds < 200)
                {
                    return;
                }
                _lastHierarchyChange = now;
            }

            RecordEvent(EventTypes.HierarchyChanged, "Scene", new Dictionary<string, object>());
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            var payload = new Dictionary<string, object>
            {
                ["state"] = state.ToString()
            };

            RecordEvent(EventTypes.PlayModeChanged, "Editor", payload);
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = path
            };

            RecordEvent(EventTypes.SceneSaving, scene.name, payload);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            var payload = new Dictionary<string, object>
            {
                ["scene_name"] = scene.name,
                ["path"] = scene.path,
                ["mode"] = mode.ToString()
            };

            RecordEvent(EventTypes.SceneOpened, scene.name, payload);
        }

        private static void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                var evt = new EditorEvent(
                    sequence: 0,  // Will be assigned by EventStore.Record
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                Core.EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityEventHooks] Failed to record event: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Asset postprocessor for tracking asset changes.
    /// Uses Unity's AssetPostprocessor callback pattern, not event subscription.
    /// Updated to use EventType constants for type safety.
    /// </summary>
    internal sealed class AssetChangePostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            foreach (var assetPath in importedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath,
                    ["extension"] = System.IO.Path.GetExtension(assetPath)
                };

                // Determine asset type
                if (assetPath.EndsWith(".cs"))
                {
                    payload["asset_type"] = "script";
                }
                else if (assetPath.EndsWith(".unity"))
                {
                    payload["asset_type"] = "scene";
                }
                else if (assetPath.EndsWith(".prefab"))
                {
                    payload["asset_type"] = "prefab";
                }
                else if (assetPath.EndsWith(".mat"))
                {
                    payload["asset_type"] = "material";
                }

                RecordEvent(EventTypes.AssetImported, assetPath, payload);
            }

            foreach (var assetPath in deletedAssets)
            {
                if (string.IsNullOrEmpty(assetPath)) continue;

                var payload = new Dictionary<string, object>
                {
                    ["path"] = assetPath
                };

                RecordEvent(EventTypes.AssetDeleted, assetPath, payload);
            }

            // Handle moved assets
            for (int i = 0; i < movedAssets.Length; i++)
            {
                if (string.IsNullOrEmpty(movedAssets[i])) continue;

                var fromPath = i < movedFromAssetPaths.Length ? movedFromAssetPaths[i] : "";
                var payload = new Dictionary<string, object>
                {
                    ["to_path"] = movedAssets[i],
                    ["from_path"] = fromPath
                };

                RecordEvent(EventTypes.AssetMoved, movedAssets[i], payload);
            }
        }

        private static void RecordEvent(string type, string targetId, Dictionary<string, object> payload)
        {
            try
            {
                var evt = new EditorEvent(
                    sequence: 0,
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: type,
                    targetId: targetId,
                    payload: payload
                );

                // AssetPostprocessor callbacks run on main thread but outside update loop.
                // Use delayCall to defer recording to main thread update, avoiding thread warnings.
                UnityEditor.EditorApplication.delayCall += () => Core.EventStore.Record(evt);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetChangePostprocessor] Failed to record event: {ex.Message}");
            }
        }
    }
}
