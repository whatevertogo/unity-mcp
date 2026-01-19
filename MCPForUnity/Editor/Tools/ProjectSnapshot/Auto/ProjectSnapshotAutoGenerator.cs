using System;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Hooks;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.ProjectSnapshot
{
    [InitializeOnLoad]
    public static class ProjectSnapshotAutoGenerator
    {
        private static bool _isInitialized = false;
        private static double _editorIdleStartTime = -1;
        private const double IdleDelaySeconds = 3.0;
        private static DateTime _lastGenerationTime = DateTime.MinValue;
        private static readonly object _throttleLock = new object();

        private static bool ShouldGenerate(int minIntervalSeconds)
        {
            lock (_throttleLock)
            {
                var elapsed = (DateTime.UtcNow - _lastGenerationTime).TotalSeconds;
                return elapsed >= minIntervalSeconds;
            }
        }

        public static void MarkGenerated()
        {
            lock (_throttleLock)
            {
                _lastGenerationTime = DateTime.UtcNow;
            }
        }

        private static double GetElapsedSeconds()
        {
            lock (_throttleLock)
            {
                return (DateTime.UtcNow - _lastGenerationTime).TotalSeconds;
            }
        }

        public static void ResetThrottle()
        {
            lock (_throttleLock)
            {
                _lastGenerationTime = DateTime.MinValue;
            }
        }

        static ProjectSnapshotAutoGenerator()
        {
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            HookRegistry.OnScriptCompiled += OnScriptCompiled;
            HookRegistry.OnScriptCompilationFailed += OnScriptCompilationFailed;
            HookRegistry.OnSceneSaved += OnSceneSaved;
            EditorApplication.update += OnEditorUpdate;

            McpLog.Info("[ProjectSnapshot-Auto] Automatic generation initialized");
        }

        private static void OnScriptCompiled()
        {
            var settings = GetSettings();
            if (settings == null || !settings.autoGenerateEnabled || !settings.autoGenerateOnCompile) return;

            McpLog.Info("[ProjectSnapshot-Auto] Script compiled, checking if snapshot update is needed...");
            ScheduleGeneration("compile", settings);
        }

        private static void OnScriptCompilationFailed(int errorCount)
        {
            _editorIdleStartTime = -1;
        }

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            var settings = GetSettings();
            if (settings == null || !settings.autoGenerateEnabled || !settings.autoGenerateOnSceneSave) return;

            McpLog.Info($"[ProjectSnapshot-Auto] Scene saved: {scene.name}, checking if snapshot update is needed...");
            ScheduleGeneration("scene_save", settings);
        }

        private static void ScheduleGeneration(string trigger, ProjectSnapshotSettings settings)
        {
            if (settings.autoGenerateOnlyIfDirty)
            {
                var options = settings.ToOptions();
                if (!ProjectSnapshotGenerator.CheckNeedsRegeneration(options))
                {
                    McpLog.Info("[ProjectSnapshot-Auto] Project unchanged since last snapshot, skipping generation.");
                    return;
                }
            }

            if (!ShouldGenerate(settings.autoGenerateMinIntervalSeconds))
            {
                var elapsed = GetElapsedSeconds();
                McpLog.Info($"[ProjectSnapshot-Auto] Throttled: {elapsed:F0}s elapsed, minimum {settings.autoGenerateMinIntervalSeconds}s required.");
                return;
            }

            if (ProjectSnapshotGenerator.IsGenerating)
            {
                McpLog.Info("[ProjectSnapshot-Auto] Generation already in progress, skipping.");
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                McpLog.Info("[ProjectSnapshot-Auto] Skipping generation during play mode transition.");
                return;
            }

            _editorIdleStartTime = EditorApplication.timeSinceStartup;
        }

        private static void OnEditorUpdate()
        {
            if (_editorIdleStartTime < 0) return;

            var idleTime = EditorApplication.timeSinceStartup - _editorIdleStartTime;
            bool isEditorIdle = !EditorApplication.isCompiling &&
                               !EditorApplication.isUpdating &&
                               !EditorApplication.isPlayingOrWillChangePlaymode;

            if (isEditorIdle && idleTime >= IdleDelaySeconds)
            {
                _editorIdleStartTime = -1;
                ExecuteGeneration();
            }
        }

        private static void ExecuteGeneration()
        {
            var settings = GetSettings();
            if (settings == null) return;

            try
            {
                var options = settings.ToOptions();

                if (settings.autoGenerateSilentMode)
                    McpLog.Info("[ProjectSnapshot-Auto] Generating snapshot in silent mode...");
                else
                    McpLog.Info("[ProjectSnapshot-Auto] Generating snapshot...");

                var result = ProjectSnapshotGenerator.Generate(options, isAutoGenerated: true);

                if (result != null && result.Success)
                {
                    MarkGenerated();
                    McpLog.Info($"[ProjectSnapshot-Auto] Snapshot generated successfully in {result.GenerationTimeMs}ms. Output: {result.OutputPath}, {result.WordCount} words.");
                }
                else if (result == null)
                {
                    McpLog.Error("[ProjectSnapshot-Auto] Generation skipped (already in progress).");
                }
                else
                {
                    McpLog.Error($"[ProjectSnapshot-Auto] Generation failed: {result.ErrorMessage}");
                }
            }
            catch (Exception e)
            {
                McpLog.Error($"[ProjectSnapshot-Auto] Error during generation: {e.Message}\n{e.StackTrace}");
            }
        }

        private static ProjectSnapshotSettings GetSettings()
        {
            try
            {
                return ProjectSnapshotSettings.Instance;
            }
            catch (Exception e)
            {
                McpLog.Warn($"[ProjectSnapshot-Auto] Could not load settings: {e.Message}");
                return null;
            }
        }

        public static SnapshotResult ForceGenerate()
        {
            var settings = GetSettings();
            if (settings == null)
            {
                McpLog.Error("[ProjectSnapshot-Auto] Settings not available for forced generation.");
                return new SnapshotResult { Success = false, ErrorMessage = "Settings not available" };
            }

            ResetThrottle();
            return ProjectSnapshotGenerator.Generate(settings.ToOptions(), isAutoGenerated: false);
        }

        public static void Shutdown()
        {
            if (!_isInitialized) return;

            HookRegistry.OnScriptCompiled -= OnScriptCompiled;
            HookRegistry.OnScriptCompilationFailed -= OnScriptCompilationFailed;
            HookRegistry.OnSceneSaved -= OnSceneSaved;
            EditorApplication.update -= OnEditorUpdate;

            _isInitialized = false;
            McpLog.Info("[ProjectSnapshot-Auto] Automatic generation shut down");
        }
    }
}
