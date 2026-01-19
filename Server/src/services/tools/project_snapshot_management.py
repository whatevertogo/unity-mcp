"""ProjectSnapshot management tools - cache status, dirty checking, and index regeneration.

This module provides tools for managing the ProjectSnapshot system cache and dependency index.
It consolidates cache operations and index management into a unified interface.

Tools:
- check_project_dirty: Check if project has changed since last snapshot
- clear_snapshot_cache: Clear snapshot and dependency index cache
- get_cache_status: Get current cache metadata and status
- regenerate_dependency_index: Regenerate dependency index for fast queries
"""

from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Checks if project has changed since last snapshot using lightweight timestamp comparison. Use before regenerating snapshots to avoid unnecessary work.",
)
async def check_project_dirty(
    ctx: Context,
    snapshot_path: Annotated[str, "Path to the snapshot file to check against. Defaults to 'Project_Snapshot.md'."] | None = None,
) -> dict[str, Any]:
    """Check if the Unity project has been modified since the last snapshot was generated.

    This lightweight operation compares file timestamps to determine if a full
    snapshot regeneration is necessary, avoiding unnecessary work when nothing has changed.

    Args:
        snapshot_path: Optional path to the snapshot file. Defaults to "Project_Snapshot.md"

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with dirty check results
            - is_dirty: bool - True if project changed
            - last_project_modified: str - ISO timestamp of last modification
            - last_snapshot_generated: str - ISO timestamp of snapshot generation
            - snapshot_age_minutes: float - Age in minutes
            - recommendation: str - Action recommendation
            - changed_areas: list[str] - List of changed area names
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {}
        if snapshot_path:
            params["snapshot_path"] = snapshot_path

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "check_project_dirty",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Dirty check complete."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error checking project dirty status: {str(e)}"}


@mcp_for_unity_tool(
    description="Clears the snapshot cache, forcing a full regeneration on next snapshot.",
    annotations=ToolAnnotations(
        title="Clear Snapshot Cache",
        destructiveHint=True,
    ),
)
async def clear_snapshot_cache(
    ctx: Context,
) -> dict[str, Any]:
    """Clear the snapshot cache and dependency index.

    This operation removes all cached snapshot data and the dependency index,
    forcing a complete regeneration on the next snapshot generation.

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with clear results
            - cache_cleared: bool - True if cache was cleared
            - index_cleared: bool - True if index was cleared
            - recommendation: str - Follow-up recommendation
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "clear_snapshot_cache",
            {},
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Cache cleared."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error clearing cache: {str(e)}"}


@mcp_for_unity_tool(
    description="Returns the current snapshot cache status including metadata.",
)
async def get_cache_status(
    ctx: Context,
) -> dict[str, Any]:
    """Get the current status of the snapshot cache.

    Retrieves metadata about the cached snapshot including generation time,
    dirty status, and cached asset counts.

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with cache status
            - cache_exists: bool - True if cache file exists
            - index_exists: bool - True if dependency index exists
            - is_dirty: bool - True if project has changed
            - last_generated: str - ISO timestamp of last generation
            - project_last_modified: str - ISO timestamp of project modification
            - cache_version: str - Cache format version
            - has_dependencies: bool - True if dependencies are cached
            - total_assets: int - Number of cached assets
            - total_prefabs: int - Number of cached prefabs
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_cache_status",
            {},
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Cache status retrieved."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting cache status: {str(e)}"}


@mcp_for_unity_tool(
    description="Regenerates the dependency index for fast queries without generating the full snapshot.",
)
async def regenerate_dependency_index(
    ctx: Context,
    settings_asset_path: Annotated[str, "Optional path to ProjectSnapshotSettings asset for configuration."] | None = None,
) -> dict[str, Any]:
    """Regenerate the dependency index.

    The dependency index enables fast asset dependency queries without requiring
    a full snapshot generation. This is useful when you only need to query
    asset relationships.

    Args:
        settings_asset_path: Optional path to a ProjectSnapshotSettings asset
                            to configure index generation behavior.

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with index results
            - index_path: str - Path to the generated index file
            - asset_count: int - Number of assets indexed
            - last_generated: str - ISO timestamp of generation
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {}
        if settings_asset_path:
            params["settings_asset_path"] = settings_asset_path

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "regenerate_dependency_index",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Dependency index regenerated."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error regenerating index: {str(e)}"}
