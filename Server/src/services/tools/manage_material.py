"""
Defines the manage_material tool for interacting with Unity materials.
"""
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.preflight import preflight, preflight_guard
from services.tools.utils import (
    normalize_color,
    normalize_param_map,
    rule_int,
    rule_json_value,
    rule_object,
)
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Manages Unity materials (set properties, colors, shaders, etc). Read-only actions: ping, get_material_info. Modifying actions: create, set_material_shader_property, set_material_color, assign_material_to_renderer, set_renderer_color.",
    annotations=ToolAnnotations(
        title="Manage Material",
        destructiveHint=True,
    ),
)
@preflight_guard(
    wait_for_no_compile=True,
    refresh_if_dirty=True,
    skip_actions={"ping", "get_material_info"},
)
async def manage_material(
    ctx: Context,
    action: Annotated[Literal[
        "ping",
        "create",
        "set_material_shader_property",
        "set_material_color",
        "assign_material_to_renderer",
        "set_renderer_color",
        "get_material_info"
    ], "Action to perform."],

    # Common / Shared
    material_path: Annotated[str,
                             "Path to material asset (Assets/...)"] | None = None,
    property: Annotated[str,
                        "Shader property name (e.g., _BaseColor, _MainTex)"] | None = None,

    # create
    shader: Annotated[str, "Shader name (default: Standard)"] | None = None,
    properties: Annotated[dict[str, Any] | str,
                          "Initial properties to set as {name: value} dict."] | None = None,

    # set_material_shader_property
    value: Annotated[list | float | int | str | bool | None,
                     "Value to set (color array, float, texture path/instruction)"] | None = None,

    # set_material_color / set_renderer_color
    color: Annotated[list[float] | dict[str, float] | str,
                     "Color as [r, g, b] or [r, g, b, a] array, {r, g, b, a} object, or JSON string."] | None = None,

    # assign_material_to_renderer / set_renderer_color
    target: Annotated[str,
                      "Target GameObject (name, path, or find instruction)"] | None = None,
    search_method: Annotated[Literal["by_name", "by_path", "by_tag",
                                     "by_layer", "by_component"], "Search method for target"] | None = None,
    slot: Annotated[int, "Material slot index (0-based)"] | None = None,
    mode: Annotated[Literal["shared", "instance", "property_block"],
                    "Assignment/modification mode"] | None = None,

) -> dict[str, Any]:
    action_name = action.lower()
    unity_instance = get_unity_instance_from_context(ctx)
    raw_slot = slot

    # --- Normalize color with validation ---
    color, color_error = normalize_color(color, output_range="float")
    if color_error:
        return {"success": False, "message": color_error}

    normalized_params, normalization_error = normalize_param_map(
        {
            "properties": properties,
            "value": value,
            "slot": slot,
        },
        [
            rule_object("properties"),
            rule_json_value("value"),
            rule_int("slot"),
        ],
    )
    if normalization_error:
        return {"success": False, "message": normalization_error}
    properties = normalized_params.get("properties") if normalized_params else None
    value = normalized_params.get("value") if normalized_params else None
    slot = normalized_params.get("slot") if normalized_params else None

    # --- Validate required parameters by action ---
    missing: list[str] = []
    if action_name in {"create", "get_material_info"}:
        if not material_path:
            missing.append("material_path")
    elif action_name == "set_material_shader_property":
        if not material_path:
            missing.append("material_path")
        if not property:
            missing.append("property")
        if value is None:
            missing.append("value")
    elif action_name == "set_material_color":
        if not material_path:
            missing.append("material_path")
        if color is None:
            missing.append("color")
    elif action_name == "assign_material_to_renderer":
        if not target:
            missing.append("target")
        if not material_path:
            missing.append("material_path")
    elif action_name == "set_renderer_color":
        if not target:
            missing.append("target")
        if color is None:
            missing.append("color")

    if missing:
        return {
            "success": False,
            "message": f"Missing required parameter(s) for action '{action_name}': {', '.join(missing)}"
        }

    # --- Normalize slot to int (reject invalid input instead of silently defaulting) ---
    if raw_slot is not None and slot is None:
        return {
            "success": False,
            "message": f"slot must be a non-negative integer, got: {raw_slot!r}"
        }
    if slot is not None and slot < 0:
        return {
            "success": False,
            "message": f"slot must be a non-negative integer, got: {slot!r}"
        }

    # Prepare parameters for the C# handler
    params_dict = {
        "action": action_name,
        "materialPath": material_path,
        "shader": shader,
        "properties": properties,
        "property": property,
        "value": value,
        "color": color,
        "target": target,
        "searchMethod": search_method,
        "slot": slot,
        "mode": mode
    }

    # Remove None values
    params_dict = {k: v for k, v in params_dict.items() if v is not None}

    # Use centralized async retry helper with instance routing
    result = await send_with_unity_instance(
        async_send_command_with_retry,
        unity_instance,
        "manage_material",
        params_dict,
    )

    return result if isinstance(result, dict) else {"success": False, "message": str(result)}
