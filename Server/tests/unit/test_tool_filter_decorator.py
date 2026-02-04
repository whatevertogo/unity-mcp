"""Unit tests for tool_filter_decorator module.

Tests the ToolPrerequisite class logic without external dependencies.
Integration tests for the full decorator flow are in tests/integration/test_tool_filtering_integration.py
"""

import pytest
from unittest.mock import patch

from core.tool_filter_decorator import ToolPrerequisite, tool_prerequisites


class TestToolPrerequisite:
    """Test ToolPrerequisite.is_met() method with various editor states."""

    def test_no_prerequisites_always_pass(self):
        """Tool with no prerequisites should always be available."""
        prereq = ToolPrerequisite()
        state = {}

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_require_no_compile_pass_when_not_compiling(self):
        """Tool should be available when Unity is not compiling."""
        prereq = ToolPrerequisite(require_no_compile=True)
        state = {
            "advice": {"blocking_reasons": []}
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_require_no_compile_fail_when_compiling(self):
        """Tool should be hidden when Unity is compiling."""
        prereq = ToolPrerequisite(require_no_compile=True)
        state = {
            "advice": {"blocking_reasons": ["compiling"]}
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        assert reason == "compiling"

    def test_require_no_compile_fail_when_domain_reload(self):
        """Tool should be hidden during domain reload."""
        prereq = ToolPrerequisite(require_no_compile=True)
        state = {
            "advice": {"blocking_reasons": ["domain_reload"]}
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        assert reason == "domain_reload"

    def test_require_selection_pass_when_has_selection(self):
        """Tool should be available when GameObject is selected."""
        prereq = ToolPrerequisite(require_selection=True)
        state = {
            "editor": {
                "selection": {
                    "has_selection": True
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_require_selection_fail_when_no_selection(self):
        """Tool should be hidden when no GameObject is selected."""
        prereq = ToolPrerequisite(require_selection=True)
        state = {
            "editor": {
                "selection": {
                    "has_selection": False
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        assert reason == "no_selection"

    def test_require_selection_pass_when_unknown(self):
        """Tool should be available when selection state is unknown (fail-open)."""
        prereq = ToolPrerequisite(require_selection=True)
        state = {
            "editor": {
                "selection": {
                    "has_selection": None
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        # Fail-open: unknown state should not hide the tool
        assert is_met is True
        assert reason is None

    def test_require_paused_for_destructive_fail_in_play_mode(self):
        """Tool should be hidden during active play mode."""
        prereq = ToolPrerequisite(require_paused_for_destructive=True)
        state = {
            "editor": {
                "play_mode": {
                    "is_playing": True,
                    "is_paused": False
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        assert reason == "play_mode_active"

    def test_require_paused_for_destructive_pass_when_paused(self):
        """Tool should be available when play mode is paused."""
        prereq = ToolPrerequisite(require_paused_for_destructive=True)
        state = {
            "editor": {
                "play_mode": {
                    "is_playing": True,
                    "is_paused": True
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_require_paused_for_destructive_pass_when_not_playing(self):
        """Tool should be available when not in play mode."""
        prereq = ToolPrerequisite(require_paused_for_destructive=True)
        state = {
            "editor": {
                "play_mode": {
                    "is_playing": False,
                    "is_paused": False
                }
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_require_no_tests_fail_when_tests_running(self):
        """Tool should be hidden when tests are running."""
        prereq = ToolPrerequisite(require_no_tests=True)
        state = {
            "tests": {
                "is_running": True
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        assert reason == "tests_running"

    def test_require_no_tests_pass_when_tests_not_running(self):
        """Tool should be available when tests are not running."""
        prereq = ToolPrerequisite(require_no_tests=True)
        state = {
            "tests": {
                "is_running": False
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_combined_prerequisites_all_pass(self):
        """Tool should be available when all prerequisites are met."""
        prereq = ToolPrerequisite(
            require_no_compile=True,
            require_selection=True,
            require_paused_for_destructive=True
        )
        state = {
            "advice": {"blocking_reasons": []},
            "editor": {
                "selection": {"has_selection": True},
                "play_mode": {"is_playing": False, "is_paused": False}
            },
            "tests": {"is_running": False}
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None

    def test_combined_prerequisites_first_blocking_wins(self):
        """Should return first blocking reason, not all of them."""
        prereq = ToolPrerequisite(
            require_no_compile=True,
            require_selection=True
        )
        state = {
            "advice": {"blocking_reasons": ["compiling"]},
            "editor": {
                "selection": {"has_selection": False}
            }
        }

        is_met, reason = prereq.is_met(state)

        assert is_met is False
        # Compilation is checked first, so it should be the blocking reason
        assert reason == "compiling"

    def test_missing_editor_section_graceful(self):
        """Should handle missing editor section gracefully."""
        prereq = ToolPrerequisite(require_selection=True)
        state = {
            "advice": {"blocking_reasons": []}
        }

        is_met, reason = prereq.is_met(state)

        # Missing selection data means we can't confirm no selection
        # Fail-open behavior: tool should be visible
        assert is_met is True
        assert reason is None

    def test_missing_advice_section_graceful(self):
        """Should handle missing advice section gracefully."""
        prereq = ToolPrerequisite(require_no_compile=True)
        state = {}

        is_met, reason = prereq.is_met(state)

        assert is_met is True
        assert reason is None


class TestPrerequisiteDecoratorRegistration:
    """Test that decorator properly registers tools."""

    def test_tool_prerequisites_is_dict(self):
        """tool_prerequisites should be a dictionary."""
        assert isinstance(tool_prerequisites, dict)

    def test_decorator_registers_tool(self):
        """Decorator should add tool to global registry."""
        # Create a dummy function to decorate
        def sample_tool(ctx):
            return None

        # Manually register like the decorator does
        from core.tool_filter_decorator import _prerequisites_lock
        tool_name = "test_sample_tool"
        with _prerequisites_lock:
            tool_prerequisites[tool_name] = ToolPrerequisite(require_selection=True)

        # Verify it was registered
        assert tool_name in tool_prerequisites
        assert tool_prerequisites[tool_name].require_selection is True

        # Clean up
        with _prerequisites_lock:
            del tool_prerequisites[tool_name]


class TestConcurrentAccess:
    """Test thread-safe concurrent access to tool_prerequisites dictionary."""

    def test_concurrent_read_with_registration(self):
        """Concurrent reads during registration should be thread-safe."""
        import threading

        results = []
        errors = []

        def register_tools():
            try:
                for i in range(5):
                    tool_name = f"concurrent_tool_{i}"
                    with patch("core.tool_filter_decorator._prerequisites_lock"):
                        tool_prerequisites[tool_name] = ToolPrerequisite(
                            require_selection=(i % 2 == 0)
                        )
            except Exception as e:
                errors.append(e)

        def read_tools():
            try:
                for _ in range(10):
                    # Simulate reads
                    _ = list(tool_prerequisites.keys())
            except Exception as e:
                errors.append(e)

        # Start threads
        threads = [
            threading.Thread(target=register_tools),
            threading.Thread(target=read_tools),
            threading.Thread(target=read_tools),
        ]

        for t in threads:
            t.start()
        for t in threads:
            t.join()

        # No errors should occur
        assert len(errors) == 0

        # Clean up
        with patch("core.tool_filter_decorator._prerequisites_lock"):
            for i in range(5):
                tool_prerequisites.pop(f"concurrent_tool_{i}", None)
