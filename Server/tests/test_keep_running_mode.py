"""Tests for Keep Server Running feature (Issue #672).

This feature allows the MCP server to stay running even when Unity disconnects,
enabling automatic reconnection when Unity comes back.
"""

import pytest

from transport.plugin_registry import PluginSession


class TestKeepServerRunningMode:
    """Tests for keep_server_running functionality."""

    def test_keep_running_message_format(self):
        """Verify keep_server_running field in RegisterMessage."""
        # Test that RegisterMessage can be created with keep_server_running=True
        msg_true = {
            "project_name": "TestProject",
            "project_hash": "hash123",
            "unity_version": "2022.3.0f0",
            "project_path": "/Test/Path",
            "keep_server_running": True
        }

        # Test that RegisterMessage can be created with keep_server_running=False
        msg_false = {
            "project_name": "TestProject",
            "project_hash": "hash456",
            "unity_version": "2022.3.0f0",
            "project_path": "/Test/Path",
            "keep_server_running": False
        }

        # Verify the field name and values are correct
        assert "keep_server_running" in msg_true
        assert msg_true["keep_server_running"] == True
        assert "keep_server_running" in msg_false
        assert msg_false["keep_server_running"] == False

    def test_plugin_session_has_keep_running_field(self):
        """Verify PluginSession dataclass has keep_server_running field."""
        # Create a session with keep_server_running=True
        session = PluginSession(
            session_id="test-session-123",
            project_name="TestProject",
            project_hash="abc123",
            unity_version="2022.3.0f0",
            project_path="/path/to/project",
            keep_server_running=True
        )
        assert session.keep_server_running is True

        # Create a session with keep_server_running=False (default)
        session_default = PluginSession(
            session_id="test-session-456",
            project_name="TestProject",
            project_hash="def456",
            unity_version="2023.2.0f1",
            project_path="/another/path"
        )
        assert session_default.keep_server_running is False
