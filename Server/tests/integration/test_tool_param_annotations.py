"""
Tests to verify tool parameter type annotations include str for JSON string support.

This ensures FastMCP/Pydantic doesn't reject JSON string inputs before our handlers
can parse them.
"""
import pytest
import typing
import types
from unittest.mock import AsyncMock, Mock
from typing import get_type_hints, get_origin, get_args


def _union_includes_str(union_type) -> bool:
    """Check if a Union type includes str."""
    args = get_args(union_type)
    return str in args


def _annotation_accepts_str(annotation) -> bool:
    """
    Check if a type annotation accepts str input.

    Returns True for:
    - str
    - str | None
    - dict | str | None
    - Any union containing str
    """
    origin = get_origin(annotation)

    # Direct str type
    if annotation is str:
        return True

    # Annotated[T, ...] -> inspect T recursively
    if origin is typing.Annotated:
        return _annotation_accepts_str(get_args(annotation)[0])

    # Union / Optional / PEP604 unions -> inspect all members recursively
    if origin is typing.Union or (hasattr(types, 'UnionType') and isinstance(annotation, types.UnionType)):
        return any(_annotation_accepts_str(arg) for arg in get_args(annotation))

    return False


def _extract_actual_type(annotation):
    """Extract real type from Annotated[T, ...] or return the annotation as-is."""
    if get_origin(annotation) is typing.Annotated:
        return get_args(annotation)[0]
    return annotation


class TestToolParamAnnotations:
    """Verify tool parameters that need JSON string support have str in their annotations."""

    def test_manage_components_properties_accepts_str(self):
        """manage_components.properties should accept str for JSON string input."""
        from services.tools.manage_components import manage_components
        hints = get_type_hints(manage_components, include_extras=True)

        annotation = hints.get('properties')
        assert annotation is not None, "properties parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"properties should accept str, got {actual_type}"

    def test_manage_prefabs_create_child_accepts_str(self):
        """manage_prefabs.create_child should accept str for JSON string input."""
        from services.tools.manage_prefabs import manage_prefabs
        hints = get_type_hints(manage_prefabs, include_extras=True)

        annotation = hints.get('create_child')
        assert annotation is not None, "create_child parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"create_child should accept str, got {actual_type}"

    def test_manage_texture_as_sprite_accepts_str(self):
        """manage_texture.as_sprite should accept str for JSON string input."""
        from services.tools.manage_texture import manage_texture
        hints = get_type_hints(manage_texture, include_extras=True)

        annotation = hints.get('as_sprite')
        assert annotation is not None, "as_sprite parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"as_sprite should accept str, got {actual_type}"

    def test_manage_texture_import_settings_accepts_str(self):
        """manage_texture.import_settings should accept str for JSON string input."""
        from services.tools.manage_texture import manage_texture
        hints = get_type_hints(manage_texture, include_extras=True)

        annotation = hints.get('import_settings')
        assert annotation is not None, "import_settings parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"import_settings should accept str, got {actual_type}"

    def test_manage_texture_set_pixels_accepts_str(self):
        """manage_texture.set_pixels should accept str for JSON string input."""
        from services.tools.manage_texture import manage_texture
        hints = get_type_hints(manage_texture, include_extras=True)

        annotation = hints.get('set_pixels')
        assert annotation is not None, "set_pixels parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"set_pixels should accept str, got {actual_type}"

    def test_manage_gameobject_component_properties_accepts_str(self):
        """manage_gameobject.component_properties should accept str for JSON string input."""
        from services.tools.manage_gameobject import manage_gameobject
        hints = get_type_hints(manage_gameobject, include_extras=True)

        annotation = hints.get('component_properties')
        assert annotation is not None, "component_properties parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"component_properties should accept str, got {actual_type}"

    def test_manage_asset_properties_accepts_str(self):
        """manage_asset.properties should accept str for JSON string input."""
        from services.tools.manage_asset import manage_asset
        hints = get_type_hints(manage_asset, include_extras=True)

        annotation = hints.get('properties')
        assert annotation is not None, "properties parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"properties should accept str, got {actual_type}"

    def test_manage_material_properties_accepts_str(self):
        """manage_material.properties should accept str for JSON string input."""
        from services.tools.manage_material import manage_material
        hints = get_type_hints(manage_material, include_extras=True)

        annotation = hints.get('properties')
        assert annotation is not None, "properties parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"properties should accept str, got {actual_type}"

    def test_execute_custom_tool_parameters_accepts_str(self):
        """execute_custom_tool.parameters should accept str for JSON string input."""
        from services.tools.execute_custom_tool import execute_custom_tool
        hints = get_type_hints(execute_custom_tool, include_extras=True)

        annotation = hints.get('parameters')
        assert annotation is not None, "parameters parameter should exist"
        actual_type = _extract_actual_type(annotation)
        assert _annotation_accepts_str(actual_type), \
            f"parameters should accept str, got {actual_type}"

    def test_annotation_accepts_str_negative_union(self):
        """Self-check: helper should return False when union does not include str."""
        assert _annotation_accepts_str(int | dict) is False


class TestToolParamAnnotationParsing:
    """Test that tools properly parse JSON string parameters at runtime."""

    @pytest.mark.asyncio
    async def test_execute_custom_tool_parameters_none_to_empty_dict(self, monkeypatch):
        """execute_custom_tool should convert None parameters to empty dict."""
        from services.tools.execute_custom_tool import execute_custom_tool
        from .test_helpers import DummyContext

        ctx = DummyContext()

        captured = {}
        async def fake_execute(self, project_id, tool_name, unity_instance, params=None, user_id=None):
            captured["project_id"] = project_id
            captured["tool_name"] = tool_name
            captured["unity_instance"] = unity_instance
            captured["params"] = params
            captured["user_id"] = user_id
            from models.models import MCPResponse
            return MCPResponse(success=True, message="OK")

        from services import custom_tool_service
        mock_service = type("MockService", (), {"execute_tool": fake_execute})()
        monkeypatch.setattr(custom_tool_service.CustomToolService, "get_instance",
                           lambda: mock_service)

        # Mock the required functions
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.get_unity_instance_from_context",
            lambda ctx: "TestInstance@123"
        )
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.resolve_project_id_for_unity_instance",
            lambda x: "test-project-id"
        )
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.get_user_id_from_context",
            lambda ctx: "test-user"
        )

        result = await execute_custom_tool(ctx=ctx, tool_name="test_tool", parameters=None)

        # Should succeed and pass empty dict to execute_tool
        assert result.success is True
        # params should be an empty dict when None is passed
        assert captured["params"] == {}

    @pytest.mark.asyncio
    async def test_execute_custom_tool_parameters_json_string(self, monkeypatch):
        """execute_custom_tool should parse JSON string parameters."""
        from services.tools.execute_custom_tool import execute_custom_tool
        from .test_helpers import DummyContext

        ctx = DummyContext()

        captured = {}
        async def fake_execute(self, project_id, tool_name, unity_instance, params=None, user_id=None):
            captured["params"] = params
            from models.models import MCPResponse
            return MCPResponse(success=True, message="OK")

        from services import custom_tool_service
        mock_service = type("MockService", (), {"execute_tool": fake_execute})()
        monkeypatch.setattr(custom_tool_service.CustomToolService, "get_instance",
                           lambda: mock_service)

        monkeypatch.setattr(
            "services.tools.execute_custom_tool.get_unity_instance_from_context",
            lambda ctx: "TestInstance@123"
        )
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.resolve_project_id_for_unity_instance",
            lambda x: "test-project-id"
        )
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.get_user_id_from_context",
            lambda ctx: "test-user"
        )

        result = await execute_custom_tool(
            ctx=ctx,
            tool_name="test_tool",
            parameters='{"key": "value", "number": 42}'
        )

        assert result.success is True
        # params should be the parsed JSON dict
        assert captured["params"] == {"key": "value", "number": 42}

    @pytest.mark.asyncio
    async def test_execute_custom_tool_parameters_string_null_returns_error(self, monkeypatch):
        """JSON string 'null' should not be treated as an object."""
        from services.tools.execute_custom_tool import execute_custom_tool
        from .test_helpers import DummyContext

        ctx = DummyContext()

        service = Mock()
        service.execute_tool = AsyncMock()

        from services import custom_tool_service
        monkeypatch.setattr(custom_tool_service.CustomToolService, "get_instance",
                           lambda: service)
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.get_unity_instance_from_context",
            lambda ctx: "TestInstance@123"
        )
        monkeypatch.setattr(
            "services.tools.execute_custom_tool.resolve_project_id_for_unity_instance",
            lambda x: "test-project-id"
        )

        result = await execute_custom_tool(
            ctx=ctx,
            tool_name="test_tool",
            parameters="null"
        )

        assert result.success is False
        assert "must be an object/dictionary" in result.message
        service.execute_tool.assert_not_awaited()
