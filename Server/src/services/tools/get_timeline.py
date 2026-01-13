"""
Defines the get_timeline tool for querying Unity editor event history.

This tool provides access to the Timeline system which tracks editor events
with human-readable summaries, semantic analysis, and optional context associations.

Unity implementation: MCPForUnity/Editor/Resources/Timeline/TimelineViewResource.cs
"""
from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_int, coerce_bool
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Queries the timeline of editor events with human-readable summaries. Supports semantic analysis (importance, category, intent), context associations, and task-level filtering. This is a read-only snapshot query of the Unity editor's event history. By default, only medium+ importance events are returned (filters out noise like HierarchyChanged). Use include_low_importance=true to see all events.",
    annotations=ToolAnnotations(
        title="Get Timeline",
    ),
)
async def get_timeline(
    ctx: Context,
    limit: Annotated[int | str, "Maximum number of events to return (1-1000, default: 50). Accepts int or string e.g., 50 or '50'."] | None = None,
    since_sequence: Annotated[int | str, "Only return events after this sequence number. Use to poll for new events since last query."] | None = None,
    include_context: Annotated[bool | str, "Include context associations (default: false). Accepts true/false or 'true'/'false'."] | None = None,
    include_semantics: Annotated[bool | str, "Include importance, category, and intent analysis (default: false). Accepts true/false or 'true'/'false'."] | None = None,
    include_low_importance: Annotated[bool | str, "Include low-importance events like HierarchyChanged (default: false). When false, only medium+ importance events are returned."] | None = None,
    task_id: Annotated[str, "Filter by task identifier. Returns only events associated with this task (e.g., AINote events with matching task_id)."] | None = None,
    conversation_id: Annotated[str, "Filter by conversation/session identifier. Returns all events from this conversation."] | None = None,
    source: Annotated[str, "Filter by operation source: 'ai', 'human', or 'system' (not yet supported)."] | None = None,
) -> dict[str, Any]:
    """
    Get timeline of editor events from Unity.

    Returns events in reverse chronological order (newest first) with:
    - sequence: Event sequence number
    - timestamp_unix_ms: Unix timestamp in milliseconds
    - type: Event type (e.g., 'create', 'modify', 'delete')
    - target: Target object identifier
    - summary: Human-readable description

    L3 Semantic Whitelist (default behavior):
    By default, only medium+ importance events are returned (importance >= 0.4).
    This filters out low-value noise like:
    - HierarchyChanged (0.2 importance)
    - PlayModeChanged (0.3 importance)

    To include all events, set include_low_importance=true.

    P1.2 Task-Level Filtering:
    Use task_id to filter events for a specific task (e.g., only AINote events from task-abc123).
    Use conversation_id to retrieve all events from a specific session/conversation.

    When include_semantics is true, also includes:
    - importance_score: Numeric importance (0-1)
    - importance_category: Category (e.g., 'low', 'medium', 'high', 'critical')
    - inferred_intent: Inferred operation intent

    When include_context is true, also includes:
    - has_context: Whether context associations exist
    - context: Context information if available

    Response schema versions:
    - timeline_view@1: Basic query
    - timeline_view@2: With context
    - timeline_view@3: With semantics
    """
    # Get active instance from request state (injected by middleware)
    unity_instance = get_unity_instance_from_context(ctx)

    # Coerce parameters defensively
    coerced_limit = coerce_int(limit, default=50)
    include_context = coerce_bool(include_context, default=False)
    include_semantics = coerce_bool(include_semantics, default=False)
    include_low_importance = coerce_bool(include_low_importance, default=False)  # L3: Default to filtering

    # Clamp limit to reasonable range
    coerced_limit = max(1, min(coerced_limit, 1000))

    # Prepare parameters for the C# handler
    params_dict: dict[str, Any] = {
        "limit": coerced_limit,
    }

    if since_sequence is not None:
        coerced_since = coerce_int(since_sequence)
        if coerced_since is not None:
            params_dict["since_sequence"] = coerced_since

    if include_context:
        params_dict["include_context"] = True

    if include_semantics:
        params_dict["include_semantics"] = True

    # L3 Semantic Whitelist: Set min_importance based on include_low_importance
    if include_low_importance:
        params_dict["min_importance"] = "low"  # Show all events
    else:
        params_dict["min_importance"] = "medium"  # Filter to medium+ (default)

    if source is not None:
        params_dict["source"] = source

    # P1.2 Task-Level Filtering
    if task_id is not None:
        params_dict["task_id"] = task_id

    if conversation_id is not None:
        params_dict["conversation_id"] = conversation_id

    # Send command using centralized retry helper with instance routing
    response = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "timeline_view",
        params_dict,
    )

    return response if isinstance(response, dict) else {"success": False, "message": str(response)}
