"""Tool filtering middleware for state-aware tool visibility.

This module provides middleware that filters the tool list based on editor state
before sending it to the LLM. Tools that don't meet their prerequisites are hidden.
"""

import logging
from typing import Any

from fastmcp import Context

from core.tool_filter_decorator import tool_prerequisites

logger = logging.getLogger("mcp-for-unity-server")


async def get_tools_matching_state(
    ctx: Context,
    all_tools: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """Filter tools based on current editor state.

    Args:
        ctx: The MCP context
        all_tools: List of all registered tool dictionaries

    Returns:
        Filtered list of tools that meet their prerequisites
    """
    try:
        from services.resources.editor_state import get_editor_state

        # Query current editor state
        state_resp = await get_editor_state(ctx)
        state_data = state_resp.data if hasattr(state_resp, "data") else None

        if not isinstance(state_data, dict):
            # Fail-safe: if we can't get state, return all tools
            logger.warning("Failed to query editor state, returning all tools (fail-safe)")
            return all_tools

        # Filter tools based on their prerequisites
        filtered_tools = []
        for tool in all_tools:
            tool_name = tool.get("name", "")
            prereq = tool_prerequisites.get(tool_name)

            if prereq is None:
                # No prerequisites - always visible
                filtered_tools.append(tool)
                continue

            # Check if prerequisites are met
            is_met, blocking_reason = prereq.is_met(state_data)

            if is_met:
                filtered_tools.append(tool)
                logger.debug(f"Tool '{tool_name}' visible: all prerequisites met")
            else:
                logger.debug(
                    f"Tool '{tool_name}' hidden: {blocking_reason}"
                )

        return filtered_tools

    except Exception as e:
        # Fail-safe: on error, return all tools
        logger.error(f"Error filtering tools: {e}, returning all tools (fail-safe)")
        return all_tools


class FilterResult:
    """Outcome of prerequisite evaluation for a tool.

    Attributes:
        tool_name: The name of the tool
        is_visible: Whether the tool should be visible
        blocking_reason: The reason why the tool is hidden (if applicable)
    """

    def __init__(
        self,
        tool_name: str,
        is_visible: bool,
        blocking_reason: str | None = None,
    ):
        self.tool_name = tool_name
        self.is_visible = is_visible
        self.blocking_reason = blocking_reason

    def to_dict(self) -> dict[str, Any]:
        return {
            "tool_name": self.tool_name,
            "is_visible": self.is_visible,
            "blocking_reason": self.blocking_reason,
        }
