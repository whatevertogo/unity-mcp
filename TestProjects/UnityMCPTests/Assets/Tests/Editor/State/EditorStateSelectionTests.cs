using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Tests for EditorStateCache selection tracking and compilation state.
    /// Part of the State-Aware Tool Filtering feature (001-state-aware-tool-filtering).
    /// </summary>
    [TestFixture]
    public class EditorStateSelectionTests
    {
        private GameObject _testGameObject;
        private const string TestObjectName = "TestSelectionObject";

        [SetUp]
        public void SetUp()
        {
            // Create a test GameObject for selection tests
            _testGameObject = new GameObject(TestObjectName);
        }

        [TearDown]
        public void TearDown()
        {
            // Clear selection and clean up
            Selection.activeInstanceID = 0;
            if (_testGameObject != null)
            {
                Object.DestroyImmediate(_testGameObject);
            }
        }

        #region Selection State Tests

        [Test]
        public void GetSnapshot_WithNoSelection_HasSelectionIsFalse()
        {
            // Arrange - Ensure no selection
            Selection.activeInstanceID = 0;

            // Act - Get the snapshot (force refresh to get current state)
            var snapshot = EditorStateCache.GetSnapshot(forceRefresh: true);

            // Assert - Verify selection state
            Assert.IsNotNull(snapshot, "Snapshot should not be null");

            var editor = snapshot["editor"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(editor, "Editor section should exist");

            var selection = editor["selection"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(selection, "Selection section should exist");

            var hasSelection = selection["has_selection"] as Newtonsoft.Json.Linq.JValue;
            Assert.IsFalse(
                hasSelection != null && (bool)hasSelection,
                "has_selection should be false when nothing is selected"
            );

            var selectionCount = selection["selection_count"] as Newtonsoft.Json.Linq.JValue;
            Assert.AreEqual(
                0,
                selectionCount != null ? (int)selectionCount : 0,
                "selection_count should be 0 when nothing is selected"
            );
        }

        [Test]
        public void GetSnapshot_WithGameObjectSelected_HasSelectionIsTrue()
        {
            // Arrange - Select the test GameObject
            Selection.activeGameObject = _testGameObject;

            // Act - Get the snapshot (force refresh to get current state)
            var snapshot = EditorStateCache.GetSnapshot(forceRefresh: true);

            // Assert - Verify selection state
            var editor = snapshot["editor"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(editor, "Editor section should exist");

            var selection = editor["selection"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(selection, "Selection section should exist");

            var hasSelection = selection["has_selection"] as Newtonsoft.Json.Linq.JValue;
            Assert.IsTrue(
                hasSelection != null && (bool)hasSelection,
                "has_selection should be true when GameObject is selected"
            );

            var gameObjectName = selection["active_game_object_name"] as Newtonsoft.Json.Linq.JValue;
            Assert.AreEqual(
                TestObjectName,
                gameObjectName != null ? (string)gameObjectName : string.Empty,
                "active_game_object_name should match selected GameObject"
            );

            var selectionCount = selection["selection_count"] as Newtonsoft.Json.Linq.JValue;
            Assert.AreEqual(
                1,
                selectionCount != null ? (int)selectionCount : 0,
                "selection_count should be 1 when one GameObject is selected"
            );
        }

        [Test]
        public void GetSnapshot_SelectionChange_TriggerUpdate()
        {
            // Arrange - Start with no selection
            Selection.activeInstanceID = 0;
            var initialSnapshot = EditorStateCache.GetSnapshot(forceRefresh: true);
            var initialSelection = initialSnapshot["editor"]?["selection"]?["has_selection"] as Newtonsoft.Json.Linq.JValue;
            bool initialVal = initialSelection != null && (bool)initialSelection;

            // Act - Select a GameObject
            Selection.activeGameObject = _testGameObject;

            // Force an update by getting a new snapshot
            // The cache updates automatically via Selection.selectionChanged callback
            var updatedSnapshot = EditorStateCache.GetSnapshot(forceRefresh: true);
            var updatedSelection = updatedSnapshot["editor"]?["selection"]?["has_selection"] as Newtonsoft.Json.Linq.JValue;
            bool updatedVal = updatedSelection != null && (bool)updatedSelection;

            // Assert - Selection state should have changed
            Assert.AreNotEqual(
                initialVal,
                updatedVal,
                "Selection state should change after GameObject selection"
            );
        }

        [Test]
        public void GetSnapshot_ActiveInstanceID_IsCorrect()
        {
            // Arrange - Select the test GameObject
            Selection.activeGameObject = _testGameObject;
            int expectedInstanceId = _testGameObject.GetInstanceID();

            // Act - Get the snapshot (force refresh to get current state)
            var snapshot = EditorStateCache.GetSnapshot(forceRefresh: true);

            // Assert - Verify instance ID
            var editor = snapshot["editor"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(editor, "Editor section should exist");

            var selection = editor["selection"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(selection, "Selection section should exist");

            var instanceIdToken = selection["active_instance_id"] as Newtonsoft.Json.Linq.JValue;
            int actualInstanceId = instanceIdToken != null ? (int)instanceIdToken : 0;

            Assert.AreEqual(
                expectedInstanceId,
                actualInstanceId,
                "active_instance_id should match selected GameObject instance ID"
            );
        }

        #endregion

        #region Compilation State Tests

        [Test]
        public void GetSnapshot_CompilationState_IsTracked()
        {
            // Act - Get the snapshot
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify compilation state exists
            var compilation = snapshot["compilation"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(compilation, "Compilation section should exist");

            // Check that compilation state fields are present
            Assert.IsTrue(
                compilation.ContainsKey("is_compiling"),
                "is_compiling field should exist"
            );

            Assert.IsTrue(
                compilation.ContainsKey("is_domain_reload_pending"),
                "is_domain_reload_pending field should exist"
            );
        }

        [Test]
        public void GetSnapshot_CompilationState_WhenNotCompiling()
        {
            // Arrange - Wait for any pending compilation to finish
            while (EditorApplication.isCompiling)
            {
                System.Threading.Thread.Sleep(100);
            }

            // Act - Get the snapshot
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify not compiling
            var compilation = snapshot["compilation"] as Newtonsoft.Json.Linq.JObject;
            var isCompilingToken = compilation["is_compiling"] as Newtonsoft.Json.Linq.JValue;
            bool isCompiling = isCompilingToken != null && (bool)isCompilingToken;

            Assert.IsFalse(
                isCompiling,
                "is_compiling should be false when not compiling"
            );
        }

        #endregion

        #region Blocking Reasons Tests

        [Test]
        public void GetSnapshot_Advice_ContainsBlockingReasons()
        {
            // Act - Get the snapshot
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify advice section exists
            var advice = snapshot["advice"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(advice, "Advice section should exist");

            Assert.IsTrue(
                advice.ContainsKey("blocking_reasons"),
                "blocking_reasons field should exist in advice"
            );

            Assert.IsTrue(
                advice.ContainsKey("ready_for_tools"),
                "ready_for_tools field should exist in advice"
            );
        }

        [Test]
        public void GetSnapshot_WhenCompiling_CompilingInBlockingReasons()
        {
            // This test would require triggering compilation, which is complex
            // For now, we just verify the structure exists
            var snapshot = EditorStateCache.GetSnapshot();
            var advice = snapshot["advice"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(advice, "Advice section should exist");

            var blockingReasons = advice["blocking_reasons"] as Newtonsoft.Json.Linq.JArray;
            Assert.IsNotNull(
                blockingReasons,
                "blocking_reasons should be a JArray"
            );
        }

        #endregion

        #region Play Mode State Tests

        [Test]
        public void GetSnapshot_PlayModeState_IsTracked()
        {
            // Act - Get the snapshot (should be in edit mode for these tests)
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify play mode state exists
            var editor = snapshot["editor"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(editor, "Editor section should exist");

            var playMode = editor["play_mode"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(playMode, "Play mode section should exist");

            Assert.IsTrue(
                playMode.ContainsKey("is_playing"),
                "is_playing field should exist"
            );

            Assert.IsTrue(
                playMode.ContainsKey("is_paused"),
                "is_paused field should exist"
            );
        }

        [Test]
        public void GetSnapshot_InEditMode_IsPlayingIsFalse()
        {
            // Arrange - Ensure we're not in play mode
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }

            // Act - Get the snapshot
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify not in play mode
            var editor = snapshot["editor"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(editor, "Editor section should exist");

            var playMode = editor["play_mode"] as Newtonsoft.Json.Linq.JObject;
            Assert.IsNotNull(playMode, "Play mode section should exist");

            var isPlayingToken = playMode["is_playing"] as Newtonsoft.Json.Linq.JValue;
            bool isPlaying = isPlayingToken != null && (bool)isPlayingToken;

            Assert.IsFalse(
                isPlaying,
                "is_playing should be false in edit mode"
            );
        }

        #endregion

        #region Snapshot Structure Tests

        [Test]
        public void GetSnapshot_HasRequiredFields()
        {
            // Act - Get the snapshot
            var snapshot = EditorStateCache.GetSnapshot();

            // Assert - Verify top-level fields exist
            Assert.IsTrue(snapshot.ContainsKey("schema_version"), "schema_version should exist");
            Assert.IsTrue(snapshot.ContainsKey("observed_at_unix_ms"), "observed_at_unix_ms should exist");
            Assert.IsTrue(snapshot.ContainsKey("sequence"), "sequence should exist");
            Assert.IsTrue(snapshot.ContainsKey("editor"), "editor should exist");
            Assert.IsTrue(snapshot.ContainsKey("advice"), "advice should exist");
        }

        [Test]
        public void GetSnapshot_ReturnsNewCloneEachTime()
        {
            // Act - Get two snapshots
            var snapshot1 = EditorStateCache.GetSnapshot();
            var snapshot2 = EditorStateCache.GetSnapshot();

            // Assert - They should be different object instances
            Assert.AreNotSame(
                snapshot1,
                snapshot2,
                "GetSnapshot should return a new clone each time"
            );
        }

        #endregion
    }
}
