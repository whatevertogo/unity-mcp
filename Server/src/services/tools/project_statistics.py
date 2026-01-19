"""Project statistics tool - provides quick project overview and metrics.

This module provides a tool for getting a quick overview of project statistics
and metrics without generating a full snapshot.

Tools:
- get_project_statistics: Returns quick project statistics and metrics
"""

from typing import Annotated, Any

from fastmcp import Context

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Returns quick project statistics including script counts, asset counts, and architectural metrics. Use for a fast project overview.",
)
async def get_project_statistics(
    ctx: Context,
    include_patterns: Annotated[bool, "If true, include detected architectural patterns."] | None = None,
) -> dict[str, Any]:
    """Get quick statistics about the Unity project.

    This lightweight tool returns key project metrics without generating
    a full snapshot. Useful for getting a quick project overview.

    Args:
        include_patterns: Include detected architectural patterns (default: true)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with project statistics
            - total_scripts: int - Total C# scripts
            - scriptable_objects: int - ScriptableObject types
            - json_files: int - JSON data files
            - scenes: int - Scene files
            - resources_folders: int - Resources folders
            - architectural_patterns: dict - Pattern counts (if requested)
                - Singletons: int
                - Events: int
                - State Machines: int
                - etc.
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {
            "include_patterns": include_patterns if include_patterns is not None else True,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_project_statistics",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Project statistics retrieved."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting statistics: {str(e)}"}
