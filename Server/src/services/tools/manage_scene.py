from typing import Annotated, Literal, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import normalize_param_map, rule_bool, rule_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight, preflight_guard


@mcp_for_unity_tool(
    description="Performs CRUD operations on Unity scenes. Read-only actions: get_hierarchy, get_active, get_build_settings, screenshot. Modifying actions: create, load, save.",
    annotations=ToolAnnotations(
        title="Manage Scene",
        destructiveHint=True,
    ),
)
@preflight_guard(wait_for_no_compile=True, refresh_if_dirty=True)
async def manage_scene(
    ctx: Context,
    action: Annotated[Literal[
        "create",
        "load",
        "save",
        "get_hierarchy",
        "get_active",
        "get_build_settings",
        "screenshot",
    ], "Perform CRUD operations on Unity scenes, and capture a screenshot."],
    name: Annotated[str, "Scene name."] | None = None,
    path: Annotated[str, "Scene path."] | None = None,
    build_index: Annotated[int | str,
                           "Unity build index (quote as string, e.g., '0')."] | None = None,
    screenshot_file_name: Annotated[str,
                                    "Screenshot file name (optional). Defaults to timestamp when omitted."] | None = None,
    screenshot_super_size: Annotated[int | str,
                                     "Screenshot supersize multiplier (integer ≥1). Optional."] | None = None,
    # --- get_hierarchy paging/safety ---
    parent: Annotated[str | int,
                      "Optional parent GameObject reference (name/path/instanceID) to list direct children."] | None = None,
    page_size: Annotated[int | str,
                         "Page size for get_hierarchy paging."] | None = None,
    cursor: Annotated[int | str,
                      "Opaque cursor for paging (offset)."] | None = None,
    max_nodes: Annotated[int | str,
                         "Hard cap on returned nodes per request (safety)."] | None = None,
    max_depth: Annotated[int | str,
                         "Accepted for forward-compatibility; current paging returns a single level."] | None = None,
    max_children_per_node: Annotated[int | str,
                                     "Child paging hint (safety)."] | None = None,
    include_transform: Annotated[bool | str,
                                 "If true, include local transform in node summaries."] | None = None,
) -> dict[str, Any]:
    # Get active instance from session state
    # Removed session_state import
    unity_instance = get_unity_instance_from_context(ctx)
    try:
        normalized_params, normalization_error = normalize_param_map(
            {
                "build_index": build_index,
                "screenshot_super_size": screenshot_super_size,
                "page_size": page_size,
                "cursor": cursor,
                "max_nodes": max_nodes,
                "max_depth": max_depth,
                "max_children_per_node": max_children_per_node,
                "include_transform": include_transform,
            },
            [
                rule_int("build_index", output_key="buildIndex"),
                rule_int("screenshot_super_size", output_key="superSize"),
                rule_int("page_size", output_key="pageSize"),
                rule_int("cursor"),
                rule_int("max_nodes", output_key="maxNodes"),
                rule_int("max_depth", output_key="maxDepth"),
                rule_int("max_children_per_node", output_key="maxChildrenPerNode"),
                rule_bool("include_transform", output_key="includeTransform"),
            ],
        )
        if normalization_error:
            return {"success": False, "message": normalization_error}

        params: dict[str, Any] = {"action": action}
        if name:
            params["name"] = name
        if path:
            params["path"] = path
        if screenshot_file_name:
            params["fileName"] = screenshot_file_name

        # get_hierarchy paging/safety params (optional)
        if parent is not None:
            params["parent"] = parent

        if normalized_params:
            params.update(normalized_params)

        # Use centralized retry helper with instance routing
        response = await send_with_unity_instance(async_send_command_with_retry, unity_instance, "manage_scene", params)

        # Preserve structured failure data; unwrap success into a friendlier shape
        if isinstance(response, dict) and response.get("success"):
            return {"success": True, "message": response.get("message", "Scene operation successful."), "data": response.get("data")}
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error managing scene: {str(e)}"}
