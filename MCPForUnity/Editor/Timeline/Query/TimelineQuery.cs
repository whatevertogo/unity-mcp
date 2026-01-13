using System;
using System.Collections.Generic;
using System.Linq;
using MCPForUnity.Editor.Timeline.Core;
using MCPForUnity.Editor.Timeline.Semantics;

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

                // Infer intent with sliding window context (prevents global bias)
                // Use a sliding window of 5 events before and after (11 total)
                var surrounding = GetSurroundingEvents(events, i, windowSize: 5);
                var intent = _inferrer.Infer(evt, surrounding);

                result[i] = new TimelineViewItem
                {
                    Event = evt,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent
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

            // Pre-extract events once (O(n)) instead of inside loop (O(n²))
            var eventsOnly = eventsWithContext.Select(x => x.Event).ToList();
            var result = new TimelineViewItem[eventsWithContext.Count];

            for (int i = 0; i < eventsWithContext.Count; i++)
            {
                var (evt, ctx) = eventsWithContext[i];

                var score = _scorer.Score(evt);
                var category = _categorizer.Categorize(score);
                
                // Infer intent with sliding window context
                var surrounding = GetSurroundingEvents(eventsOnly, i, windowSize: 5);
                var intent = _inferrer.Infer(evt, surrounding);

                result[i] = new TimelineViewItem
                {
                    Event = evt,
                    Context = ctx,
                    ImportanceScore = score,
                    ImportanceCategory = category,
                    InferredIntent = intent
                };
            }

            return result;
        }

        /// <summary>
        /// Helper method to get surrounding events within a sliding window.
        /// This ensures intent inference has true temporal locality rather than
        /// being biased by the entire query window.
        /// </summary>
        private static IReadOnlyList<EditorEvent> GetSurroundingEvents(
            IReadOnlyList<EditorEvent> allEvents, int currentIndex, int windowSize)
        {
            if (allEvents == null || allEvents.Count == 0)
                return Array.Empty<EditorEvent>();

            int start = Math.Max(0, currentIndex - windowSize);
            int end = Math.Min(allEvents.Count - 1, currentIndex + windowSize);
            int count = end - start + 1;

            // Create a slice of the events list
            var result = new List<EditorEvent>(count);
            for (int i = start; i <= end; i++)
            {
                result.Add(allEvents[i]);
            }
            return result;
        }
    }

    /// <summary>
    /// A view of an event with projected semantic information.
    /// This is a computed projection, not stored data.
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
    }
}
