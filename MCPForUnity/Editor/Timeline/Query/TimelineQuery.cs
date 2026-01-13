using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Timeline.Core;
using MCPForUnity.Editor.Timeline.Semantics;
using UnityEngine;

namespace MCPForUnity.Editor.Timeline.Query
{
    /// <summary>
    /// Query engine that projects events with semantic information.
    /// All semantic data (importance, category, intent) is computed at query time
    /// and does not modify the original events.
    /// </summary>
    public sealed class TimelineQuery
    {
        private readonly IEventScorer _scorer;
        private readonly IEventCategorizer _categorizer;
        private readonly IIntentInferrer _inferrer;

        /// <summary>
        /// Create a new TimelineQuery with optional custom semantic components.
        /// If null, default implementations are used.
        /// </summary>
        public TimelineQuery(
            IEventScorer scorer = null,
            IEventCategorizer categorizer = null,
            IIntentInferrer inferrer = null)
        {
            _scorer = scorer ?? new DefaultEventScorer();
            _categorizer = categorizer ?? new DefaultCategorizer();
            _inferrer = inferrer ?? new DefaultIntentInferrer();
        }

        /// <summary>
        /// Project events with computed semantic information.
        /// Returns TimelineViewItem objects containing the original event plus
        /// dynamically calculated importance, category, and intent.
        /// </summary>
        public IReadOnlyList<TimelineViewItem> Project(IReadOnlyList<EditorEvent> events)
        {
            if (events == null || events.Count == 0)
                return Array.Empty<TimelineViewItem>();

            var result = new TimelineViewItem[events.Count];

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];

                // Compute importance score
                var score = _scorer.Score(evt);

                // Categorize the score
                var category = _categorizer.Categorize(score);

                // 计算上下文窗口（当前事件前后各5个事件）用于意图推断
                int contextWindow = 5;
                int contextStart = Math.Max(0, i - contextWindow);
                int contextEnd = Math.Min(events.Count, i + contextWindow + 1);
                int contextLength = contextEnd - contextStart;

                EditorEvent[] surrounding = null;
                if (contextLength > 0)
                {
                    surrounding = new EditorEvent[contextLength];

                    // 性能考虑：EventStore 查询通常按时间顺序追加（但 Query 返回可能为降序）。
                    // 我们在 O(1) 检测顺序（首/末 sequence 比较），并在需要时按时间顺序填充 surrounding
                    bool isDescending = events.Count > 1 && events[0].Sequence > events[events.Count - 1].Sequence;

                    if (!isDescending)
                    {
                        for (int j = 0; j < contextLength; j++)
                        {
                            surrounding[j] = events[contextStart + j];
                        }
                    }
                    else
                    {
                        // events 为降序（最新在前），需要按时间升序（最旧->最新）构建 surrounding
                        // 填充方式：从 contextEnd-1 向下遍历到 contextStart，产生升序窗口
                        for (int j = 0; j < contextLength; j++)
                        {
                            surrounding[j] = events[contextEnd - 1 - j];
                        }
                    }
                }

                // 使用 surrounding 参数进行意图推断（按时间升序）
                var intent = _inferrer.Infer(evt, surrounding);

                // 使用 EditorEvent 的 GetSummary() 方法，它会自动处理脱水事件
                var displaySummary = evt.GetSummary();
                var displaySummaryLower = (displaySummary ?? string.Empty).ToLowerInvariant();
                var displayTargetIdLower = (evt.TargetId ?? string.Empty).ToLowerInvariant();
                var displayTime = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampUnixMs).ToString("HH:mm:ss");
                var displaySequence = evt.Sequence.ToString();

                // 预计算颜色
                var typeColor = GetEventTypeColor(evt.Type);
                var importanceColor = GetImportanceColor(category);
                var importanceBadgeColor = GetImportanceBadgeColor(category);

                result[i] = new TimelineViewItem
                {
                    Event = evt,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent,
                    // 设置显示缓存
                    DisplaySummary = displaySummary,
                    DisplaySummaryLower = displaySummaryLower,
                    DisplayTargetIdLower = displayTargetIdLower,
                    DisplayTime = displayTime,
                    DisplaySequence = displaySequence,
                    TypeColor = typeColor,
                    ImportanceColor = importanceColor,
                    ImportanceBadgeColor = importanceBadgeColor
                };
            }

            return result;
        }

        /// <summary>
        /// Project events with context associations.
        /// Overload for QueryWithContext results.
        /// </summary>
        public IReadOnlyList<TimelineViewItem> ProjectWithContext(
            IReadOnlyList<(EditorEvent Event, Context.ContextMapping Context)> eventsWithContext)
        {
            if (eventsWithContext == null || eventsWithContext.Count == 0)
                return Array.Empty<TimelineViewItem>();

            var result = new TimelineViewItem[eventsWithContext.Count];

            for (int i = 0; i < eventsWithContext.Count; i++)
            {
                var (evt, ctx) = eventsWithContext[i];

                var score = _scorer.Score(evt);
                var category = _categorizer.Categorize(score);

                // Use simple inference to avoid List allocation
                var intent = _inferrer.Infer(evt, surrounding: null);

                // 使用 EditorEvent 的 GetSummary() 方法，它会自动处理脱水事件
                var displaySummary = evt.GetSummary();
                var displaySummaryLower = (displaySummary ?? string.Empty).ToLowerInvariant();
                var displayTargetIdLower = (evt.TargetId ?? string.Empty).ToLowerInvariant();
                var displayTime = DateTimeOffset.FromUnixTimeMilliseconds(evt.TimestampUnixMs).ToString("HH:mm:ss");
                var displaySequence = evt.Sequence.ToString();

                // 预计算颜色
                var typeColor = GetEventTypeColor(evt.Type);
                var importanceColor = GetImportanceColor(category);
                var importanceBadgeColor = GetImportanceBadgeColor(category);

                result[i] = new TimelineViewItem
                {
                    Event = evt,
                    Context = ctx,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent,
                    // 设置显示缓存
                    DisplaySummary = displaySummary,
                    DisplaySummaryLower = displaySummaryLower,
                    DisplayTargetIdLower = displayTargetIdLower,
                    DisplayTime = displayTime,
                    DisplaySequence = displaySequence,
                    TypeColor = typeColor,
                    ImportanceColor = importanceColor,
                    ImportanceBadgeColor = importanceBadgeColor
                };
            }

            return result;
        }

        /// <summary>
        /// Get event type color for display.
        /// </summary>
        private static Color GetEventTypeColor(string eventType)
        {
            return eventType switch
            {
                "ComponentAdded" => new Color(0.3f, 0.8f, 0.3f),
                "PropertyModified" => new Color(0.3f, 0.6f, 0.8f),
                "GameObjectCreated" => new Color(0.8f, 0.3f, 0.8f),
                "HierarchyChanged" => new Color(0.8f, 0.8f, 0.3f),
                "AINote" => new Color(0.3f, 0.8f, 0.8f),
                _ => Color.gray
            };
        }

        /// <summary>
        /// Get importance background color (nullable).
        /// </summary>
        private static Color? GetImportanceColor(string category)
        {
            return category switch
            {
                "critical" => new Color(1f, 0.3f, 0.3f, 0.1f),
                "high" => new Color(1f, 0.6f, 0f, 0.08f),
                "medium" => new Color(1f, 1f, 0.3f, 0.06f),
                "low" => null,
                _ => null
            };
        }

        /// <summary>
        /// Get importance badge color.
        /// </summary>
        private static Color GetImportanceBadgeColor(string category)
        {
            return category switch
            {
                "critical" => new Color(0.8f, 0.2f, 0.2f),
                "high" => new Color(1f, 0.5f, 0f),
                "medium" => new Color(1f, 0.8f, 0.2f),
                "low" => new Color(0.5f, 0.5f, 0.5f),
                _ => Color.gray
            };
        }

        /// <summary>
        /// A view of an event with projected semantic information.
        /// This is a computed projection, not stored data.
        ///
        /// 性能优化：所有显示字符串在投影时预计算，避免 OnGUI 中的重复分配。
        /// </summary>
        public sealed class TimelineViewItem
        {
            /// <summary>
            /// The original immutable event.
            /// </summary>
            public EditorEvent Event { get; set; }

            /// <summary>
            /// Optional context association (may be null).
            /// </summary>
            public Context.ContextMapping Context { get; set; }

            /// <summary>
            /// Computed importance score (0.0 to 1.0).
            /// Higher values indicate more important events.
            /// </summary>
            public float ImportanceScore { get; set; }

            /// <summary>
            /// Category label derived from importance score.
            /// Values: "critical", "high", "medium", "low"
            /// </summary>
            public string ImportanceCategory { get; set; }

            /// <summary>
            /// Inferred user intent or purpose.
            /// May be null if intent cannot be determined.
            /// </summary>
            public string InferredIntent { get; set; }

            // ========== 显示缓存（避免 OnGUI 中的重复分配）==========

            /// <summary>
            /// 预计算的事件摘要（用于显示）
            /// </summary>
            public string DisplaySummary { get; set; }

            /// <summary>
            /// 预计算的摘要小写（用于搜索过滤）
            /// </summary>
            public string DisplaySummaryLower { get; set; }

            /// <summary>
            /// 预计算的目标 ID 小写（用于搜索过滤）
            /// </summary>
            public string DisplayTargetIdLower { get; set; }

            /// <summary>
            /// 预计算的格式化时间（HH:mm:ss）
            /// </summary>
            public string DisplayTime { get; set; }

            /// <summary>
            /// 预计算的序列号字符串
            /// </summary>
            public string DisplaySequence { get; set; }

            /// <summary>
            /// 预计算的类型颜色（避免渲染时 switch）
            /// </summary>
            public Color TypeColor { get; set; }

            /// <summary>
            /// 预计算的重要性颜色
            /// </summary>
            public Color? ImportanceColor { get; set; }

            /// <summary>
            /// 预计算的重要性徽章颜色
            /// </summary>
            public Color ImportanceBadgeColor { get; set; }
        }
    }
}
