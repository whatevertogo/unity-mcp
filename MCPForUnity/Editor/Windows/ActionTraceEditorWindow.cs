using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.ActionTrace.Core;
using MCPForUnity.Editor.ActionTrace.Query;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static MCPForUnity.Editor.ActionTrace.Query.ActionTraceQuery;

namespace MCPForUnity.Editor.Windows
{
    public sealed class ActionTraceEditorWindow : EditorWindow
    {
        // UI
        private ListView eventListView;
        private ScrollView detailScrollView;
        private Label statusLabel;
        private Label countLabel;
        private ToolbarSearchField searchField;
        private ToolbarToggle importanceToggle;
        private ToolbarToggle contextToggle;
        private ToolbarMenu filterMenu;
        private ToolbarButton settingsButton;
        private ToolbarButton refreshButton;
        private ToolbarButton clearButton;

        // Data
        private readonly List<ActionTraceViewItem> currentEvents = new();
        private ActionTraceQuery timelineQuery;

        private string searchText = string.Empty;
        private float minImportance;  // Default: AI can see (uses ActionTraceSettings.MinImportanceForRecording)
        private bool showSemantics;
        private bool showContext;

        private double lastRefreshTime;
        private const double RefreshInterval = 1.0;

        public static void ShowWindow()
        {
            var window = GetWindow<ActionTraceEditorWindow>("TimeLine");
            window.minSize = new Vector2(900, 600);
        }

        private void CreateGUI()
        {
            var basePath = AssetPathUtility.GetMcpPackageRootPath();

            // Try the expected package-relative path first
            if (string.IsNullOrEmpty(basePath))
            {
                // Could not determine package root; search project for the UXML asset
                var guids = AssetDatabase.FindAssets("ActionTraceEditorWindow t:VisualTreeAsset");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var uxmlFound = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                    if (uxmlFound != null)
                    {
                        uxmlFound.CloneTree(rootVisualElement);
                        SetupReferences();
                        SetupListView();
                        SetupToolbar();
                        timelineQuery = new ActionTraceQuery();
                        RefreshEvents();
                        UpdateStatus();
                        return;
                    }
                }

                Debug.LogError("Could not determine MCP package root path and ActionTraceEditorWindow.uxml was not found in project.");
                return;
            }

            var expectedPath = $"{basePath}/Editor/Windows/ActionTraceEditorWindow.uxml";
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(expectedPath);

            // Fallback: try sanitized Assets/... path or search by asset name if not found
            if (uxml == null)
            {
                // Try sanitize in case basePath is a relative path without Assets/ prefix
                var sanitized = AssetPathUtility.SanitizeAssetPath(expectedPath);
                if (!string.Equals(sanitized, expectedPath, StringComparison.Ordinal) )
                {
                    uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(sanitized);
                }
            }

            if (uxml == null)
            {
                // Search for any matching asset in the project as a last resort
                var guids = AssetDatabase.FindAssets("ActionTraceEditorWindow t:VisualTreeAsset");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                }
            }

            if (uxml == null)
            {
                Debug.LogError($"ActionTraceEditorWindow.uxml not found (tried: {expectedPath})");
                return;
            }

            uxml.CloneTree(rootVisualElement);

            // Verify UXML loaded successfully
            if (rootVisualElement.childCount == 0)
            {
                Debug.LogError("ActionTraceEditorWindow: UXML loaded but rootVisualElement is empty. Check UXML structure.");
                return;
            }

            SetupReferences();

            // Validate all required UI elements
            if (eventListView == null)
            {
                Debug.LogError("ActionTraceEditorWindow: 'event-list' ListView not found in UXML.");
                return;
            }
            if (detailScrollView == null)
            {
                Debug.LogError("ActionTraceEditorWindow: 'detail-scroll-view' ScrollView not found in UXML.");
                return;
            }
            if (statusLabel == null)
            {
                Debug.LogError("ActionTraceEditorWindow: 'status-label' Label not found in UXML.");
                return;
            }
            if (countLabel == null)
            {
                Debug.LogError("ActionTraceEditorWindow: 'count-label' Label not found in UXML.");
                return;
            }

            SetupListView();
            SetupToolbar();

            timelineQuery = new ActionTraceQuery();

            // Initialize minImportance from ActionTraceSettings (AI can see)
            minImportance = ActionTraceSettings.Instance?.MinImportanceForRecording ?? 0.4f;

            RefreshEvents();
            UpdateStatus();
        }

        private void SetupReferences()
        {
            eventListView = rootVisualElement.Q<ListView>("event-list");
            detailScrollView = rootVisualElement.Q<ScrollView>("detail-scroll-view");
            statusLabel = rootVisualElement.Q<Label>("status-label");
            countLabel = rootVisualElement.Q<Label>("count-label");

            searchField = rootVisualElement.Q<ToolbarSearchField>("search-field");
            importanceToggle = rootVisualElement.Q<ToolbarToggle>("importance-toggle");
            contextToggle = rootVisualElement.Q<ToolbarToggle>("context-toggle");
            filterMenu = rootVisualElement.Q<ToolbarMenu>("filter-menu");
            settingsButton = rootVisualElement.Q<ToolbarButton>("settings-button");
            refreshButton = rootVisualElement.Q<ToolbarButton>("refresh-button");
            clearButton = rootVisualElement.Q<ToolbarButton>("clear-button");
        }

        private void SetupListView()
        {
            eventListView.itemsSource = currentEvents;
            eventListView.selectionType = SelectionType.Single;

            eventListView.makeItem = () =>
            {
                var root = new VisualElement();
                root.AddToClassList("event-item");

                var time = new Label { name = "time" };
                time.AddToClassList("event-time");
                root.Add(time);

                var type = new Label { name = "type" };
                type.AddToClassList("event-type");
                root.Add(type);

                var summary = new Label { name = "summary" };
                summary.AddToClassList("event-summary");
                root.Add(summary);

                var badge = new Label { name = "badge" };
                badge.AddToClassList("importance-badge");
                root.Add(badge);

                return root;
            };


            eventListView.bindItem = (e, i) =>
            {
                var item = currentEvents[i];

                e.Q<Label>("time").text = item.DisplayTime;
                e.Q<Label>("type").text = item.Event.Type;
                e.Q<Label>("summary").text = item.DisplaySummary;

                var badge = e.Q<Label>("badge");
                badge.style.display = showSemantics ? DisplayStyle.Flex : DisplayStyle.None;
                badge.text = item.ImportanceCategory.ToUpperInvariant();
                badge.style.backgroundColor = item.ImportanceBadgeColor;

                e.EnableInClassList("has-context", item.Context != null);
            };

            eventListView.selectionChanged += OnSelectionChanged;
        }

        private void SetupToolbar()
        {
            searchField?.RegisterValueChangedCallback(e =>
            {
                searchText = e.newValue.ToLowerInvariant();
                RefreshEvents();
            });

            importanceToggle?.RegisterValueChangedCallback(e =>
            {
                showSemantics = e.newValue;
                eventListView.RefreshItems();
            });

            contextToggle?.RegisterValueChangedCallback(e =>
            {
                showContext = e.newValue;
                RefreshEvents();
            });

            filterMenu?.menu.AppendAction("AI Can See", _ => SetImportanceFromSettings());
            filterMenu?.menu.AppendAction("Low+", _ => SetImportance(0f));
            filterMenu?.menu.AppendAction("Medium+", _ => SetImportance(0.4f));
            filterMenu?.menu.AppendAction("High+", _ => SetImportance(0.7f));

            settingsButton?.RegisterCallback<ClickEvent>(_ => OnSettingsClicked());
            refreshButton?.RegisterCallback<ClickEvent>(_ => OnRefreshClicked());
            clearButton?.RegisterCallback<ClickEvent>(_ => OnClearClicked());
        }

        private void SetImportance(float value)
        {
            minImportance = value;
            RefreshEvents();
        }

        private void SetImportanceFromSettings()
        {
            var settings = ActionTraceSettings.Instance;
            minImportance = settings != null ? settings.MinImportanceForRecording : 0.4f;
            RefreshEvents();
        }

        private void OnRefreshClicked()
        {
            // Force immediate refresh and update lastRefreshTime
            lastRefreshTime = EditorApplication.timeSinceStartup;
            RefreshEvents();
            Debug.Log("[ActionTraceEditorWindow] Refresh clicked - events refreshed");
        }

        private void OnSettingsClicked()
        {
            // Open the ActionTraceSettings inspector
            ActionTraceSettings.ShowSettingsWindow();
        }

        private void OnClearClicked()
        {
            // Clear all events from EventStore
            EventStore.Clear();
            // Clear current display
            currentEvents.Clear();
            eventListView.RefreshItems();
            detailScrollView.Clear();
            UpdateStatus();
            Debug.Log("[ActionTraceEditorWindow] Clear clicked - all events cleared");
        }

        private void RefreshEvents()
        {
            IEnumerable<ActionTraceViewItem> source = showContext
                ? timelineQuery.ProjectWithContext(EventStore.QueryWithContext(200))
                : timelineQuery.Project(EventStore.Query(200));

            currentEvents.Clear();
            currentEvents.AddRange(source.Where(FilterEvent));

            eventListView.RefreshItems();
            UpdateStatus();
        }

        private bool FilterEvent(ActionTraceViewItem e)
        {
            if (e.ImportanceScore < minImportance)
                return false;

            if (!string.IsNullOrEmpty(searchText))
            {
                return e.DisplaySummaryLower.Contains(searchText)
                    || e.DisplayTargetIdLower.Contains(searchText)
                    || e.Event.Type.ToLowerInvariant().Contains(searchText);
            }

            return true;
        }

        private void OnSelectionChanged(IEnumerable<object> items)
        {
            detailScrollView.Clear();

            var item = items.FirstOrDefault() as ActionTraceViewItem;
            if (item == null) return;

            var container = new VisualElement();
            container.AddToClassList("detail-container");

            AddDetail(container, "Sequence", item.Event.Sequence.ToString());
            AddDetail(container, "Type", item.Event.Type);
            AddDetail(container, "Summary", item.DisplaySummary);

            detailScrollView.Add(container);
        }

        private static void AddDetail(VisualElement parent, string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("detail-row");

            var keyLabel = new Label { text = key };
            keyLabel.AddToClassList("detail-label");

            var valueLabel = new Label { text = value };
            valueLabel.AddToClassList("detail-value");

            row.Add(keyLabel);
            row.Add(valueLabel);
            parent.Add(row);
        }

        private void UpdateStatus()
        {
            countLabel.text = $"Events: {currentEvents.Count}";
            statusLabel.text = $"Updated: {DateTime.Now:HH:mm:ss}";
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup - lastRefreshTime > RefreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                RefreshEvents();
            }
        }
    }
}
