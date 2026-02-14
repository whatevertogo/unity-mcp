"""
Tests for JSON string parameter parsing in manage_material tool.
"""
import pytest

from .test_helpers import DummyContext
from services.tools.manage_material import manage_material


class TestManageMaterialJsonParsing:
    """Test JSON string parameter parsing functionality for properties."""

    @pytest.mark.asyncio
    async def test_properties_json_string_parsing(self, monkeypatch):
        """Test that JSON string properties are correctly parsed to dict."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Material created successfully"}

        monkeypatch.setattr(
            "services.tools.manage_material.async_send_command_with_retry",
            fake_send,
        )

        # Test with JSON string properties
        result = await manage_material(
            ctx=ctx,
            action="create",
            material_path="Assets/Materials/Test.mat",
            properties='{"_BaseColor": [1, 0, 0, 1], "_Metallic": 0.5}'
        )

        assert result["success"] is True
        assert captured["params"]["properties"] == {"_BaseColor": [1, 0, 0, 1], "_Metallic": 0.5}

    @pytest.mark.asyncio
    async def test_properties_dict_passthrough(self, monkeypatch):
        """Test that dict properties are passed through unchanged."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Material created successfully"}

        monkeypatch.setattr(
            "services.tools.manage_material.async_send_command_with_retry",
            fake_send,
        )

        # Test with dict properties
        properties_dict = {"_BaseColor": [1, 0, 0, 1], "_Metallic": 0.5}
        result = await manage_material(
            ctx=ctx,
            action="create",
            material_path="Assets/Materials/Test.mat",
            properties=properties_dict
        )

        assert result["success"] is True
        assert captured["params"]["properties"] == properties_dict

    @pytest.mark.asyncio
    async def test_properties_invalid_json_string(self, monkeypatch):
        """Test handling of invalid JSON string properties."""
        ctx = DummyContext()

        async def fake_send(cmd, params, **kwargs):
            return {"success": True}

        monkeypatch.setattr(
            "services.tools.manage_material.async_send_command_with_retry",
            fake_send,
        )

        # Test with invalid JSON string
        result = await manage_material(
            ctx=ctx,
            action="create",
            material_path="Assets/Materials/Test.mat",
            properties='{invalid json}'
        )

        # Should fail with error message
        assert result.get("success") is False
        assert "properties" in result.get("message", "").lower()

    @pytest.mark.asyncio
    async def test_properties_none_handling(self, monkeypatch):
        """Test that None properties are handled correctly."""
        ctx = DummyContext()

        captured = {}
        async def fake_send(cmd, params, **kwargs):
            captured["params"] = params
            return {"success": True, "message": "Material info retrieved"}

        monkeypatch.setattr(
            "services.tools.manage_material.async_send_command_with_retry",
            fake_send,
        )

        # Test with None properties
        result = await manage_material(
            ctx=ctx,
            action="get_material_info",
            material_path="Assets/Materials/Test.mat",
            properties=None
        )

        assert result["success"] is True
        # properties should not be in params when None
        assert "properties" not in captured["params"]

    @pytest.mark.asyncio
    async def test_properties_placeholder_values_rejected(self, monkeypatch):
        """Test that placeholder values like [object Object] are rejected."""
        ctx = DummyContext()

        async def fake_send(cmd, params, **kwargs):
            return {"success": True}

        monkeypatch.setattr(
            "services.tools.manage_material.async_send_command_with_retry",
            fake_send,
        )

        # Test with [object Object] placeholder
        result = await manage_material(
            ctx=ctx,
            action="create",
            material_path="Assets/Materials/Test.mat",
            properties="[object Object]"
        )

        assert result.get("success") is False
        assert "properties" in result.get("message", "").lower()
