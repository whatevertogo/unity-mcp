"""
Tests for JSON string parameter parsing in manage_prefabs tool.
"""
import pytest

from .test_helpers import DummyContext
from services.tools.manage_prefabs import manage_prefabs


class TestManagePrefabsJsonParsing:
    """Test JSON string parameter parsing functionality for create_child."""

    @pytest.mark.asyncio
    async def test_create_child_single_json_string(self, monkeypatch):
        """Test that single create_child JSON string is correctly parsed."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Prefab modified successfully"}

        monkeypatch.setattr(
            "services.tools.manage_prefabs.async_send_command_with_retry",
            fake_send,
        )

        # Test with JSON string for single child
        result = await manage_prefabs(
            ctx=ctx,
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            create_child='{"name": "Child1", "primitive_type": "Cube", "position": [1, 2, 3]}'
        )

        assert result["success"] is True
        assert "createChild" in captured["params"]
        assert captured["params"]["createChild"]["name"] == "Child1"

    @pytest.mark.asyncio
    async def test_create_child_array_json_string(self, monkeypatch):
        """Test that array of create_child JSON string is correctly parsed."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Prefab modified successfully"}

        monkeypatch.setattr(
            "services.tools.manage_prefabs.async_send_command_with_retry",
            fake_send,
        )

        # Test with JSON string for array of children
        result = await manage_prefabs(
            ctx=ctx,
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            create_child='[{"name": "Child1"}, {"name": "Child2"}]'
        )

        assert result["success"] is True
        assert "createChild" in captured["params"]
        assert isinstance(captured["params"]["createChild"], list)
        assert len(captured["params"]["createChild"]) == 2

    @pytest.mark.asyncio
    async def test_create_child_dict_passthrough(self, monkeypatch):
        """Test that dict create_child is passed through unchanged."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Prefab modified successfully"}

        monkeypatch.setattr(
            "services.tools.manage_prefabs.async_send_command_with_retry",
            fake_send,
        )

        # Test with dict
        result = await manage_prefabs(
            ctx=ctx,
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            create_child={"name": "Child1", "primitive_type": "Sphere"}
        )

        assert result["success"] is True
        assert captured["params"]["createChild"]["name"] == "Child1"

    @pytest.mark.asyncio
    async def test_create_child_invalid_json_string(self, monkeypatch):
        """Test handling of invalid JSON string create_child."""
        ctx = DummyContext()

        async def fake_send(cmd, params, **kwargs):
            return {"success": True}

        monkeypatch.setattr(
            "services.tools.manage_prefabs.async_send_command_with_retry",
            fake_send,
        )

        # Test with invalid JSON string
        result = await manage_prefabs(
            ctx=ctx,
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            create_child='{invalid json}'
        )

        # Should fail with error message
        assert result.get("success") is False
        assert "create_child" in result.get("message", "").lower()

    @pytest.mark.asyncio
    async def test_create_child_json_string_with_vectors(self, monkeypatch):
        """Test that JSON string create_child with vector fields works."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Prefab modified successfully"}

        monkeypatch.setattr(
            "services.tools.manage_prefabs.async_send_command_with_retry",
            fake_send,
        )

        # Test with JSON string containing vectors
        result = await manage_prefabs(
            ctx=ctx,
            action="modify_contents",
            prefab_path="Assets/Prefabs/Test.prefab",
            create_child='{"name": "Child1", "position": [1, 2, 3], "rotation": [0, 90, 0], "scale": [2, 2, 2]}'
        )

        assert result["success"] is True
        assert captured["params"]["createChild"]["position"] == [1.0, 2.0, 3.0]
