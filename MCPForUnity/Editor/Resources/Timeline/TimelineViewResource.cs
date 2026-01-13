using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Timeline.Context;
using MCPForUnity.Editor.Timeline.Core;
using MCPForUnity.Editor.Timeline.Query;
using MCPForUnity.Editor.Timeline.Semantics;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Resources.Timeline
{
    /// <summary>
    /// MCP resource for querying the timeline of editor events.
    ///
    /// URI: mcpforunity://timeline_view
    ///
    /// Parameters:
    ///   - limit: Maximum number of events to return (default: 50)
    ///   - since_sequence: Only return events after this sequence number
    ///   - include_context: If true, include context associations (default: false)
    ///   - include_semantics: If true, include importance, category, intent (default: false)
    ///   - source: Filter by operation source: "ai", "human", "system" (optional)
    /// </summary>
    [McpForUnityResource("timeline_view")]
    public static class TimelineViewResource
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                int limit = GetLimit(@params);
                long? sinceSequence = GetSinceSequence(@params);
                bool includeContext = GetIncludeContext(@params);
                bool includeSemantics = GetIncludeSemantics(@params);
                string sourceFilter = GetSourceFilter(@params);

                // Decide query mode based on parameters
                bool useContextQuery = includeContext || !string.IsNullOrEmpty(sourceFilter);

                if (useContextQuery)
                {
                    return QueryWithContext(limit, sinceSequence, sourceFilter, includeSemantics);
                }

                if (includeSemantics)
                {
                    return QueryWithSemanticsOnly(limit, sinceSequence);
                }

                // Basic query without context or semantics
                return QueryBasic(limit, sinceSequence);
            }
            catch (Exception ex)
            {
                McpLog.Error($"[TimelineViewResource] Error: {ex.Message}");
                return new ErrorResponse($"Error retrieving timeline: {ex.Message}");
            }
        }

        /// <summary>
        /// Basic query without context or semantics
        /// </summary>
        private static object QueryBasic(int limit, long? sinceSequence)
        {
            var events = EventStore.Query(limit, sinceSequence);

            var items = events.Select(e => new
            {
                sequence = e.Sequence,
                timestamp_unix_ms = e.TimestampUnixMs,
                type = e.Type,
                target = e.TargetId,
                summary = EventSummarizer.Summarize(e)
            }).ToArray();

            return new SuccessResponse("Retrieved timeline events.", new
            {
                schema_version = "timeline_view@1",
                items = items,
                total = items.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with semantics but without context
        /// </summary>
        private static object QueryWithSemanticsOnly(int limit, long? sinceSequence)
        {
            var events = EventStore.Query(limit, sinceSequence);
            var query = new TimelineQuery();
            var projected = query.Project(events);

            var items = projected.Select(p => new
            {
                sequence = p.Event.Sequence,
                timestamp_unix_ms = p.Event.TimestampUnixMs,
                type = p.Event.Type,
                target = p.Event.TargetId,
                summary = EventSummarizer.Summarize(p.Event),
                importance_score = p.ImportanceScore,
                importance_category = p.ImportanceCategory,
                inferred_intent = p.InferredIntent
            }).ToArray();

            return new SuccessResponse("Retrieved timeline events with semantics.", new
            {
                schema_version = "timeline_view@3",
                items = items,
                total = items.Length,
                current_sequence = EventStore.CurrentSequence
            });
        }

        /// <summary>
        /// Query with context and optional semantics
        /// Note: Source filtering requires persistent OperationContext storage.
        /// </summary>
        private static object QueryWithContext(int limit, long? sinceSequence, string sourceFilter, bool includeSemantics)
        {
            // Check if source filter is requested (requires persistent context storage)
            if (!string.IsNullOrEmpty(sourceFilter))
            {
                return new ErrorResponse(
                    "Source filtering requires persistent OperationContext storage. " +
                    "The 'source' parameter is not yet supported."
                );
            }

            var eventsWithContext = EventStore.QueryWithContext(limit, sinceSequence);

            // Apply semantics if requested
            if (includeSemantics)
            {
                var query = new TimelineQuery();
                var projected = query.ProjectWithContext(eventsWithContext);

                var items = projected.Select(p =>
                {
                    var hasContext = p.Context != null;
                    var contextId = hasContext ? p.Context.ContextId.ToString() : null;

                    return new
                    {
                        sequence = p.Event.Sequence,
                        timestamp_unix_ms = p.Event.TimestampUnixMs,
                        type = p.Event.Type,
                        target = p.Event.TargetId,
                        summary = EventSummarizer.Summarize(p.Event),
                        has_context = hasContext,
                        context = hasContext ? new { context_id = contextId } : null,
                        importance_score = p.ImportanceScore,
                        importance_category = p.ImportanceCategory,
                        inferred_intent = p.InferredIntent
                    };
                }).ToArray();

                return new SuccessResponse("Retrieved timeline events with context and semantics.", new
                {
                    schema_version = "timeline_view@3",
                    items = items,
                    total = items.Length,
                    current_sequence = EventStore.CurrentSequence,
                    context_mapping_count = EventStore.ContextMappingCount
                });
            }
            else
            {
                // Context only response
                var items = eventsWithContext.Select(x =>
                {
                    var hasContext = x.Context != null;
                    var contextId = hasContext ? x.Context.ContextId.ToString() : null;

                    return new
                    {
                        sequence = x.Event.Sequence,
                        timestamp_unix_ms = x.Event.TimestampUnixMs,
                        type = x.Event.Type,
                        target = x.Event.TargetId,
                        summary = EventSummarizer.Summarize(x.Event),
                        has_context = hasContext,
                        context = hasContext ? new { context_id = contextId } : null
                    };
                }).ToArray();

                return new SuccessResponse("Retrieved timeline events with context.", new
                {
                    schema_version = "timeline_view@2",
                    items = items,
                    total = items.Length,
                    current_sequence = EventStore.CurrentSequence,
                    context_mapping_count = EventStore.ContextMappingCount
                });
            }
        }

        private static int GetLimit(JObject @params)
        {
            var limitToken = @params["limit"] ?? @params["count"];
            if (limitToken != null && int.TryParse(limitToken.ToString(), out int limit))
            {
                return Math.Clamp(limit, 1, 1000);
            }
            return 50; // Default
        }

        private static long? GetSinceSequence(JObject @params)
        {
            var sinceToken = @params["since_sequence"] ?? @params["sinceSequence"] ?? @params["since"];
            if (sinceToken != null && long.TryParse(sinceToken.ToString(), out long since))
            {
                return since;
            }
            return null;
        }

        private static bool GetIncludeContext(JObject @params)
        {
            var includeToken = @params["include_context"] ?? @params["includeContext"];
            if (includeToken != null)
            {
                if (bool.TryParse(includeToken.ToString(), out bool include))
                {
                    return include;
                }
            }
            return false;
        }

        private static string GetSourceFilter(JObject @params)
        {
            var sourceToken = @params["source"] ?? @params["operation_source"];
            return sourceToken?.ToString();
        }

        private static bool GetIncludeSemantics(JObject @params)
        {
            var includeToken = @params["include_semantics"] ?? @params["includeSemantics"];
            if (includeToken != null)
            {
                if (bool.TryParse(includeToken.ToString(), out bool include))
                {
                    return include;
                }
            }
            return false;
        }
    }
}
