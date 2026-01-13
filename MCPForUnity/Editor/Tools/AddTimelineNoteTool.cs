using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Timeline.Core;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// MCP Tool for adding AI comments/notes to the Timeline.
    ///
    /// Usage: AI agents call this tool to record summaries, decisions, or task completion notes.
    ///
    /// Multi-Agent Collaboration:
    /// - task_id: Groups all notes from a single task (e.g., "refactor-player-movement")
    /// - conversation_id: Tracks continuity across sessions
    /// - agent_id: Identifies which AI wrote the note
    ///
    /// Example payload:
    /// {
    ///   "note": "完成玩家移动系统的重构，速度从 5 提升到 8",
    ///   "agent_id": "claude-opus-4.5",
    ///   "intent": "refactoring",
    ///   "task_id": "task-abc123",
    ///   "conversation_id": "conv-xyz789",
    ///   "related_sequences": [100, 101, 102]
    /// }
    /// </summary>
    [McpForUnityTool("add_timeline_note")]
    public static class AddTimelineNoteTool
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                // Required parameters
                string note = @params["note"]?.ToString();
                if (string.IsNullOrEmpty(note))
                {
                    return new ErrorResponse("Note text is required.");
                }

                string agentId = @params["agent_id"]?.ToString() ?? "unknown";
                string taskId = @params["task_id"]?.ToString();
                string conversationId = @params["conversation_id"]?.ToString();

                // Build payload with all fields
                var payload = new Dictionary<string, object>
                {
                    ["note"] = note,
                    ["agent_id"] = agentId
                };

                // Task-level tracking (P1.2 multi-agent collaboration)
                if (!string.IsNullOrEmpty(taskId))
                {
                    payload["task_id"] = taskId;
                }

                // Conversation-level tracking (cross-session continuity)
                if (!string.IsNullOrEmpty(conversationId))
                {
                    payload["conversation_id"] = conversationId;
                }

                // Optional fields
                if (@params["intent"] != null)
                {
                    payload["intent"] = @params["intent"].ToString();
                }

                if (@params["agent_model"] != null)
                {
                    payload["agent_model"] = @params["agent_model"].ToString();
                }

                // Related event sequences (if explicitly linking to specific events)
                if (@params["related_sequences"] != null)
                {
                    try
                    {
                        var relatedSeqs = @params["related_sequences"].ToObject<long[]>();
                        if (relatedSeqs != null && relatedSeqs.Length > 0)
                        {
                            payload["related_sequences"] = relatedSeqs;
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[AddTimelineNoteTool] Failed to parse related_sequences: {ex.Message}");
                    }
                }

                // Record the AINote event
                var evt = new EditorEvent(
                    sequence: 0,  // Assigned by EventStore.Record()
                    timestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    type: "AINote",  // P1.2: AI notes are always critical importance
                    targetId: $"agent:{agentId}",
                    payload: payload
                );

                long recordedSequence = EventStore.Record(evt);

                return new SuccessResponse($"AI note added to timeline (sequence {recordedSequence})", new
                {
                    sequence = recordedSequence,
                    timestamp_unix_ms = evt.TimestampUnixMs,
                    task_id = taskId,
                    conversation_id = conversationId
                });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[AddTimelineNoteTool] Error: {ex.Message}");
                return new ErrorResponse($"Failed to add timeline note: {ex.Message}");
            }
        }
    }
}
