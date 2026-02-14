"""
Tool for searching GameObjects in Unity scenes.
Returns only instance IDs with pagination support for efficient searches.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from pydantic import Field
from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.utils import normalize_param_map, rule_bool, rule_int
from services.tools.preflight import preflight, preflight_guard


@mcp_for_unity_tool(
    description=(
        "Search for GameObjects in the scene by name, tag, layer, component type, or path. "
        "Returns instance IDs only (paginated). "
        "Then use mcpforunity://scene/gameobject/{id} resource for full data, "
        "or mcpforunity://scene/gameobject/{id}/components for component details. "
        "For CRUD operations (create/modify/delete), use manage_gameobject instead."
    )
)
@preflight_guard(wait_for_no_compile=True, refresh_if_dirty=True)
async def find_gameobjects(
    ctx: Context,
    search_term: Annotated[
        str,
        Field(description="The value to search for (name, tag, layer name, component type, or path)")
    ],
    search_method: Annotated[
        Literal["by_name", "by_tag", "by_layer", "by_component", "by_path", "by_id"],
        Field(
            default="by_name",
            description="How to search for GameObjects"
        )
    ] = "by_name",
    include_inactive: Annotated[
        bool | str | None,
        Field(
            default=None,
            description="Include inactive GameObjects in search"
        )
    ] = None,
    page_size: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Number of results per page (default: 50, max: 500)"
        )
    ] = None,
    cursor: Annotated[
        int | str | None,
        Field(
            default=None,
            description="Pagination cursor (offset for next page)"
        )
    ] = None,
) -> dict[str, Any]:
    """
    Search for GameObjects and return their instance IDs.

    This is a focused search tool optimized for finding GameObjects efficiently.
    It returns only instance IDs to minimize payload size.

    For detailed GameObject information, use the returned IDs with:
    - mcpforunity://scene/gameobject/{id} - Get full GameObject data
    - mcpforunity://scene/gameobject/{id}/components - Get all components
    - mcpforunity://scene/gameobject/{id}/component/{name} - Get specific component
    """
    unity_instance = get_unity_instance_from_context(ctx)

    # Validate required parameters before preflight I/O
    if not search_term:
        return {
            "success": False,
            "message": "Missing required parameter 'search_term'. Specify what to search for."
        }

    normalized_params, normalization_error = normalize_param_map(
        {
            "include_inactive": include_inactive,
            "page_size": page_size,
            "cursor": cursor,
        },
        [
            rule_bool("include_inactive", output_key="includeInactive", default=False),
            rule_int("page_size", output_key="pageSize", default=50),
            rule_int("cursor", default=0),
        ],
    )
    if normalization_error:
        return {"success": False, "message": normalization_error}

    try:
        params = {
            "searchMethod": search_method,
            "searchTerm": search_term,
        }
        if normalized_params:
            params.update(normalized_params)
        params = {k: v for k, v in params.items() if v is not None}

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "find_gameobjects",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Search completed."),
                "data": response.get("data")
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error searching GameObjects: {e!s}"}
