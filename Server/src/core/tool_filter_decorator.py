"""Tool filter decorator for state-aware prerequisite checking.

This module provides the prerequisite_check decorator that allows tools to declare
their dependencies on Unity editor state (compilation, selection, play mode, etc.).
"""

import functools
import inspect
import logging
import threading
from typing import Callable, Any, Final

from models import MCPResponse

logger = logging.getLogger("mcp-for-unity-server")

__all__ = ["ToolPrerequisite", "prerequisite_check", "tool_prerequisites"]


class ToolPrerequisite:
    """Defines conditions under which a tool is available.

    Args:
        require_no_compile: Tool hidden when Unity is compiling
        require_selection: Tool hidden when no GameObject is selected
        require_paused_for_destructive: Tool hidden during play mode (unless paused)
        require_no_tests: Tool hidden while tests are running
    """

    def __init__(
        self,
        require_no_compile: bool = False,
        require_selection: bool = False,
        require_paused_for_destructive: bool = False,
        require_no_tests: bool = False,
    ):
        self.require_no_compile = require_no_compile
        self.require_selection = require_selection
        self.require_paused_for_destructive = require_paused_for_destructive
        self.require_no_tests = require_no_tests

    def is_met(self, editor_state: dict) -> tuple[bool, str | None]:
        """Evaluate if prerequisites are met.

        Args:
            editor_state: The current editor state from get_editor_state()

        Returns:
            (is_met, blocking_reason) tuple
        """
        advice = editor_state.get("advice", {})
        blocking_reasons = advice.get("blocking_reasons", []) if isinstance(advice, dict) else []
        editor = editor_state.get("editor", {})
        selection = editor.get("selection", {}) if isinstance(editor, dict) else {}
        play_mode = editor.get("play_mode", {}) if isinstance(editor, dict) else {}
        tests = editor_state.get("tests", {}) if isinstance(editor_state, dict) else {}

        # Check compilation prerequisite
        if self.require_no_compile:
            if "compiling" in blocking_reasons:
                return False, "compiling"

        # Check domain reload prerequisite
        if self.require_no_compile:
            if "domain_reload" in blocking_reasons:
                return False, "domain_reload"

        # Check tests prerequisite
        if self.require_no_tests:
            if isinstance(tests, dict) and tests.get("is_running") is True:
                return False, "tests_running"

        # Check selection prerequisite
        # Only hide if we know there's no selection (fail-open if state is unknown)
        if self.require_selection:
            if isinstance(selection, dict):
                has_selection = selection.get("has_selection")
                if has_selection is False:
                    return False, "no_selection"

        # Check paused for destructive operations
        if self.require_paused_for_destructive:
            if isinstance(play_mode, dict):
                is_playing = play_mode.get("is_playing")
                is_paused = play_mode.get("is_paused")
                if is_playing is True and is_paused is False:
                    return False, "play_mode_active"

        return True, None


# Global storage for tool prerequisites
# Key: tool name, Value: ToolPrerequisite instance
# Thread-safe: use _prerequisites_lock for all modifications
# Note: `Final` ensures the reference is not rebound; the dict contents remain mutable
_prerequisites_lock = threading.Lock()
tool_prerequisites: Final[dict[str, ToolPrerequisite]] = {}


def prerequisite_check(
    require_no_compile: bool = False,
    require_selection: bool = False,
    require_paused_for_destructive: bool = False,
    require_no_tests: bool = False,
) -> Callable:
    """Decorator that adds prerequisite checks to MCP tools.

    The decorator stores the prerequisite rules and evaluates them before tool execution.
    If prerequisites are not met, the tool call returns an MCPResponse with an error.

    Args:
        require_no_compile: Tool hidden when Unity is compiling
        require_selection: Tool hidden when no GameObject is selected
        require_paused_for_destructive: Tool hidden during play mode (unless paused)
        require_no_tests: Tool hidden while tests are running

    Usage:
        @prerequisite_check(require_no_compile=True, require_selection=True)
        @mcp_for_unity_tool(description="Modify selected GameObject")
        async def my_tool(ctx: Context, ...):
            ...

    Returns:
        Decorator function
    """

    def decorator(func: Callable) -> Callable:
        # Store prerequisites for this tool (used by filtering middleware)
        tool_name = func.__name__
        with _prerequisites_lock:
            tool_prerequisites[tool_name] = ToolPrerequisite(
                require_no_compile=require_no_compile,
                require_selection=require_selection,
                require_paused_for_destructive=require_paused_for_destructive,
                require_no_tests=require_no_tests,
            )

        @functools.wraps(func)
        def _sync_wrapper(*args, **kwargs) -> Any:
            # Check prerequisites before executing
            # Note: For direct tool calls, we check here
            # For tool list filtering, the middleware handles it
            ctx = None
            if args and hasattr(args[0], "get_state"):
                ctx = args[0]
            elif "ctx" in kwargs:
                ctx = kwargs["ctx"]

            if ctx:
                try:
                    from services.resources.editor_state import get_editor_state
                    import asyncio

                    # Run async get_editor_state in sync context
                    loop = None
                    try:
                        loop = asyncio.get_event_loop()
                    except RuntimeError:
                        loop = asyncio.new_event_loop()
                        asyncio.set_event_loop(loop)

                    if loop and not loop.is_closed():
                        state_resp = loop.run_until_complete(get_editor_state(ctx))
                        state_data = state_resp.data if hasattr(state_resp, "data") else None
                        if isinstance(state_data, dict):
                            # Reuse the already-registered ToolPrerequisite instance
                            prereq = tool_prerequisites.get(tool_name)
                            if prereq is not None:
                                is_met, blocking_reason = prereq.is_met(state_data)
                                if not is_met:
                                    advice = state_data.get("advice", {})
                                    blocking_reasons = advice.get("blocking_reasons", []) if isinstance(advice, dict) else []
                                    from models import MCPResponse
                                    return MCPResponse(
                                        success=False,
                                        error="prerequisite_failed",
                                        message=f"Tool '{tool_name}' is not available: {blocking_reason}",
                                        data={
                                            "tool": tool_name,
                                            "blocking_reason": blocking_reason,
                                            "current_state": {
                                                "ready_for_tools": advice.get("ready_for_tools"),
                                                "blocking_reasons": blocking_reasons,
                                            } if isinstance(state_data.get("advice"), dict) else None
                                        }
                                    )
                except RuntimeError as e:
                    # Event loop is already running in another thread - proceed (fail-safe)
                    logger.warning(f"Event loop conflict checking prerequisites for '{tool_name}': {e}")
                except Exception as e:
                    # If we can't check prerequisites, proceed (fail-safe)
                    logger.warning(f"Failed to check prerequisites for '{tool_name}': {e}")

            # Prerequisites met or couldn't check - proceed with original function
            return func(*args, **kwargs)

        @functools.wraps(func)
        async def _async_wrapper(*args, **kwargs) -> Any:
            # Check prerequisites before executing
            ctx = None
            if args and hasattr(args[0], "get_state"):
                ctx = args[0]
            elif "ctx" in kwargs:
                ctx = kwargs["ctx"]

            if ctx:
                try:
                    from services.resources.editor_state import get_editor_state

                    state_resp = await get_editor_state(ctx)
                    state_data = state_resp.data if hasattr(state_resp, "data") else None
                    if isinstance(state_data, dict):
                        # Reuse the already-registered ToolPrerequisite instance
                        prereq = tool_prerequisites.get(tool_name)
                        if prereq is not None:
                            is_met, blocking_reason = prereq.is_met(state_data)
                            if not is_met:
                                advice = state_data.get("advice", {})
                                blocking_reasons = advice.get("blocking_reasons", []) if isinstance(advice, dict) else []
                                from models import MCPResponse
                                return MCPResponse(
                                    success=False,
                                    error="prerequisite_failed",
                                    message=f"Tool '{tool_name}' is not available: {blocking_reason}",
                                    data={
                                        "tool": tool_name,
                                        "blocking_reason": blocking_reason,
                                        "current_state": {
                                            "ready_for_tools": advice.get("ready_for_tools"),
                                            "blocking_reasons": blocking_reasons,
                                        } if isinstance(state_data.get("advice"), dict) else None
                                    }
                                )
                except Exception as e:
                    # If we can't check prerequisites, proceed (fail-safe)
                    logger.warning(f"Failed to check prerequisites for '{tool_name}': {e}")

            # Prerequisites met or couldn't check - proceed with original function
            return await func(*args, **kwargs)

        return _async_wrapper if inspect.iscoroutinefunction(func) else _sync_wrapper

    return decorator
