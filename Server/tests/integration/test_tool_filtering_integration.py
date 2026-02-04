"""Integration tests for tool filtering middleware.

Tests the complete flow from editor state query to tool filtering.
"""

import pytest
from unittest.mock import AsyncMock, MagicMock, patch
from fastmcp import Context

from services.filter_middleware import get_tools_matching_state, FilterResult
from core.tool_filter_decorator import tool_prerequisites, ToolPrerequisite, prerequisite_check


class TestCompilationFiltering:
    """Test that tools are hidden during compilation."""

    @pytest.mark.asyncio
    async def test_tool_hidden_when_compiling(self):
        """Tools with require_no_compile should be hidden during compilation."""
        # Setup: Register a tool that requires no compilation
        tool_name = "test_modify_script"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(require_no_compile=True)

        # Mock editor state with compilation in progress
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {
                "blocking_reasons": ["compiling"],
                "ready_for_tools": False
            },
            "editor": {
                "selection": {"has_selection": True}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            # All tools list
            all_tools = [
                {"name": tool_name, "description": "Modify script"},
                {"name": "read_only_tool", "description": "Read something"}
            ]

            # Run filtering
            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            # The compilation-sensitive tool should be hidden
            filtered_names = [t["name"] for t in filtered]
            assert tool_name not in filtered_names
            assert "read_only_tool" in filtered_names

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)

    @pytest.mark.asyncio
    async def test_tool_visible_when_not_compiling(self):
        """Tools with require_no_compile should be visible when not compiling."""
        tool_name = "test_modify_script"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(require_no_compile=True)

        # Mock editor state without compilation
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {
                "blocking_reasons": [],
                "ready_for_tools": True
            },
            "editor": {
                "selection": {"has_selection": True}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": tool_name, "description": "Modify script"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            # Tool should be visible
            assert len(filtered) == 1
            assert filtered[0]["name"] == tool_name

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)


class TestSelectionFiltering:
    """Test that tools requiring selection are hidden when nothing selected."""

    @pytest.mark.asyncio
    async def test_tool_hidden_without_selection(self):
        """Tools with require_selection should be hidden when no selection."""
        tool_name = "test_adjust_transform"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(require_selection=True)

        # Mock editor state with no selection
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {
                "selection": {"has_selection": False}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": tool_name, "description": "Adjust transform"},
                {"name": "read_only_tool", "description": "Read something"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            filtered_names = [t["name"] for t in filtered]
            assert tool_name not in filtered_names
            assert "read_only_tool" in filtered_names

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)

    @pytest.mark.asyncio
    async def test_tool_visible_with_selection(self):
        """Tools with require_selection should be visible when GameObject selected."""
        tool_name = "test_adjust_transform"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(require_selection=True)

        # Mock editor state with selection
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {
                "selection": {"has_selection": True}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": tool_name, "description": "Adjust transform"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            assert len(filtered) == 1
            assert filtered[0]["name"] == tool_name

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)


class TestPlayModeFiltering:
    """Test that destructive tools are hidden during active play mode."""

    @pytest.mark.asyncio
    async def test_destructive_tool_hidden_in_play_mode(self):
        """Destructive tools should be hidden during active play mode."""
        tool_name = "test_delete_gameobject"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(
                require_selection=True,
                require_paused_for_destructive=True
            )

        # Mock editor state in active play mode
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {
                "selection": {"has_selection": True},
                "play_mode": {"is_playing": True, "is_paused": False}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": tool_name, "description": "Delete GameObject"},
                {"name": "safe_tool", "description": "Safe operation"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            filtered_names = [t["name"] for t in filtered]
            assert tool_name not in filtered_names
            assert "safe_tool" in filtered_names

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)

    @pytest.mark.asyncio
    async def test_destructive_tool_visible_when_paused(self):
        """Destructive tools should be visible when play mode is paused."""
        tool_name = "test_delete_gameobject"
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites[tool_name] = ToolPrerequisite(
                require_selection=True,
                require_paused_for_destructive=True
            )

        # Mock editor state in paused play mode
        mock_ctx = MagicMock(spec=Context)
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {
                "selection": {"has_selection": True},
                "play_mode": {"is_playing": True, "is_paused": True}
            }
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": tool_name, "description": "Delete GameObject"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            assert len(filtered) == 1
            assert filtered[0]["name"] == tool_name

        # Cleanup
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)


class TestFailsafeBehavior:
    """Test fail-safe behavior when state query fails."""

    @pytest.mark.asyncio
    async def test_returns_all_tools_on_state_query_error(self):
        """When state query fails, all tools should be returned (fail-safe)."""
        # Mock context
        mock_ctx = MagicMock(spec=Context)

        # Mock get_editor_state to raise exception
        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_get_state.side_effect = Exception("Unity connection lost")

            all_tools = [
                {"name": "tool1", "description": "Tool 1"},
                {"name": "tool2", "description": "Tool 2"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            # Should return all tools (fail-safe)
            assert len(filtered) == 2
            assert filtered[0]["name"] == "tool1"
            assert filtered[1]["name"] == "tool2"

    @pytest.mark.asyncio
    async def test_returns_all_tools_on_invalid_state_data(self):
        """When state data is invalid, all tools should be returned."""
        mock_ctx = MagicMock(spec=Context)

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = None  # Invalid data
            mock_get_state.return_value = mock_resp

            all_tools = [
                {"name": "tool1", "description": "Tool 1"}
            ]

            filtered = await get_tools_matching_state(mock_ctx, all_tools)

            # Should return all tools (fail-safe)
            assert len(filtered) == 1


class TestFilterResult:
    """Test FilterResult data class."""

    def test_to_dict_contains_all_fields(self):
        """FilterResult.to_dict() should contain all fields."""
        result = FilterResult(
            tool_name="test_tool",
            is_visible=False,
            blocking_reason="compiling"
        )

        result_dict = result.to_dict()

        assert result_dict["tool_name"] == "test_tool"
        assert result_dict["is_visible"] is False
        assert result_dict["blocking_reason"] == "compiling"

    def test_to_dict_with_none_blocking_reason(self):
        """FilterResult.to_dict() should handle None blocking_reason."""
        result = FilterResult(
            tool_name="test_tool",
            is_visible=True,
            blocking_reason=None
        )

        result_dict = result.to_dict()

        assert result_dict["blocking_reason"] is None


class TestAsyncDecoratorWrapper:
    """Test the async wrapper path of the prerequisite_check decorator.

    These are integration tests because they verify the complete decorator flow
    including interaction with get_editor_state.
    """

    @pytest.mark.asyncio
    async def test_async_wrapper_prereq_met(self):
        """Async tool should execute when prerequisites are met."""
        # Create a mock async tool
        @prerequisite_check(require_selection=True)
        async def mock_async_tool(ctx):
            return "tool_executed"

        # Mock context with editor state
        mock_ctx = MagicMock()
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {"selection": {"has_selection": True}}
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            result = await mock_async_tool(mock_ctx)

            # Tool should execute successfully
            assert result == "tool_executed"

        # Clean up
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop("mock_async_tool", None)

    @pytest.mark.asyncio
    async def test_async_wrapper_prereq_not_met(self):
        """Async tool should return error response when prerequisites not met."""
        @prerequisite_check(require_selection=True)
        async def mock_async_tool(ctx):
            return "should_not_execute"

        mock_ctx = MagicMock()
        mock_state = {
            "advice": {"blocking_reasons": [], "ready_for_tools": True},
            "editor": {"selection": {"has_selection": False}}
        }

        with patch("services.resources.editor_state.get_editor_state", new_callable=AsyncMock) as mock_get_state:
            mock_resp = MagicMock()
            mock_resp.data = mock_state
            mock_get_state.return_value = mock_resp

            result = await mock_async_tool(mock_ctx)

            # Should return MCPResponse error, not execute tool
            assert hasattr(result, "success")
            assert result.success is False
            assert result.error == "prerequisite_failed"
            assert "no_selection" in result.message

        # Clean up
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop("mock_async_tool", None)

    @pytest.mark.asyncio
    async def test_async_wrapper_reuses_registered_prerequisite(self):
        """Async wrapper should reuse the registered ToolPrerequisite instance."""
        @prerequisite_check(
            require_no_compile=True,
            require_selection=True,
            require_paused_for_destructive=True,
            require_no_tests=True
        )
        async def mock_tool(ctx):
            return "executed"

        tool_name = "mock_tool"
        prereq = tool_prerequisites.get(tool_name)

        # Verify the registered instance has all flags set
        assert prereq is not None
        assert prereq.require_no_compile is True
        assert prereq.require_selection is True
        assert prereq.require_paused_for_destructive is True
        assert prereq.require_no_tests is True

        # Clean up
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            tool_prerequisites.pop(tool_name, None)
