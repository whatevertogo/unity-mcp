from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import (
    normalize_object_or_list,
    normalize_param_map,
    rule_bool,
    rule_vector3,
)
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry
from services.tools.preflight import preflight, preflight_guard


# Required parameters for each action
REQUIRED_PARAMS = {
    "get_info": ["prefab_path"],
    "get_hierarchy": ["prefab_path"],
    "create_from_gameobject": ["target", "prefab_path"],
    "modify_contents": ["prefab_path"],
}


@mcp_for_unity_tool(
    description=(
        "Manages Unity Prefab assets via headless operations (no UI, no prefab stages). "
        "Actions: get_info, get_hierarchy, create_from_gameobject, modify_contents. "
        "Use modify_contents for headless prefab editing - ideal for automated workflows. "
        "Use create_child parameter with modify_contents to add child GameObjects to a prefab "
        "(single object or array for batch creation in one save). "
        "Example: create_child=[{\"name\": \"Child1\", \"primitive_type\": \"Sphere\", \"position\": [1,0,0]}, "
        "{\"name\": \"Child2\", \"primitive_type\": \"Cube\", \"parent\": \"Child1\"}]. "
        "Use manage_asset action=search filterType=Prefab to list prefabs."
    ),
    annotations=ToolAnnotations(
        title="Manage Prefabs",
        destructiveHint=True,
    ),
)
@preflight_guard(wait_for_no_compile=True, refresh_if_dirty=True)
async def manage_prefabs(
    ctx: Context,
    action: Annotated[
        Literal[
            "create_from_gameobject",
            "get_info",
            "get_hierarchy",
            "modify_contents",
        ],
        "Prefab operation to perform.",
    ],
    prefab_path: Annotated[str, "Prefab asset path (e.g., Assets/Prefabs/MyPrefab.prefab)."] | None = None,
    target: Annotated[str, "Target GameObject: scene object for create_from_gameobject, or object within prefab for modify_contents (name or path like 'Parent/Child')."] | None = None,
    allow_overwrite: Annotated[bool, "Allow replacing existing prefab."] | None = None,
    search_inactive: Annotated[bool, "Include inactive GameObjects in search."] | None = None,
    unlink_if_instance: Annotated[bool, "Unlink from existing prefab before creating new one."] | None = None,
    # modify_contents parameters
    position: Annotated[list[float] | dict[str, float] | str, "New local position [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    rotation: Annotated[list[float] | dict[str, float] | str, "New local rotation (euler angles) [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    scale: Annotated[list[float] | dict[str, float] | str, "New local scale [x, y, z] or {x, y, z} for modify_contents."] | None = None,
    name: Annotated[str, "New name for the target object in modify_contents."] | None = None,
    tag: Annotated[str, "New tag for the target object in modify_contents."] | None = None,
    layer: Annotated[str, "New layer name for the target object in modify_contents."] | None = None,
    set_active: Annotated[bool, "Set active state of target object in modify_contents."] | None = None,
    parent: Annotated[str, "New parent object name/path within prefab for modify_contents."] | None = None,
    components_to_add: Annotated[list[str], "Component types to add in modify_contents."] | None = None,
    components_to_remove: Annotated[list[str], "Component types to remove in modify_contents."] | None = None,
    create_child: Annotated[dict[str, Any] | list[dict[str, Any]] | str, "Create child GameObject(s) in the prefab. Single object or array of objects, each with: name (required), parent (optional, defaults to target), primitive_type (optional: Cube, Sphere, Capsule, Cylinder, Plane, Quad), position, rotation, scale, components_to_add, tag, layer, set_active."] | None = None,
) -> dict[str, Any]:
    # Back-compat: map 'name' → 'target' for create_from_gameobject (Unity accepts both)
    if action == "create_from_gameobject" and target is None and name is not None:
        target = name

    # Validate required parameters
    required = REQUIRED_PARAMS.get(action, [])
    for param_name in required:
        # Use updated local value for target after back-compat mapping
        param_value = target if param_name == "target" else locals().get(param_name)
        # Check for None and empty/whitespace strings
        if param_value is None or (isinstance(param_value, str) and not param_value.strip()):
            return {
                "success": False,
                "message": f"Action '{action}' requires parameter '{param_name}'."
            }

    unity_instance = get_unity_instance_from_context(ctx)

    try:
        normalized_params, normalization_error = normalize_param_map(
            {
                "allow_overwrite": allow_overwrite,
                "search_inactive": search_inactive,
                "unlink_if_instance": unlink_if_instance,
                "position": position,
                "rotation": rotation,
                "scale": scale,
                "set_active": set_active,
            },
            [
                rule_bool("allow_overwrite", output_key="allowOverwrite"),
                rule_bool("search_inactive", output_key="searchInactive"),
                rule_bool("unlink_if_instance", output_key="unlinkIfInstance"),
                rule_vector3("position"),
                rule_vector3("rotation"),
                rule_vector3("scale"),
                rule_bool("set_active", output_key="setActive"),
            ],
        )
        if normalization_error:
            return {"success": False, "message": normalization_error}

        # Build parameters dictionary
        params: dict[str, Any] = {"action": action}

        # Handle prefab path parameter
        if prefab_path:
            params["prefabPath"] = prefab_path

        if target:
            params["target"] = target

        if normalized_params:
            params.update(normalized_params)

        # modify_contents parameters
        if name is not None:
            params["name"] = name
        if tag is not None:
            params["tag"] = tag
        if layer is not None:
            params["layer"] = layer
        if parent is not None:
            params["parent"] = parent
        if components_to_add is not None:
            params["componentsToAdd"] = components_to_add
        if components_to_remove is not None:
            params["componentsToRemove"] = components_to_remove
        if create_child is not None:
            create_child, create_child_error = normalize_object_or_list(
                create_child,
                "create_child",
            )
            if create_child_error:
                return {"success": False, "message": create_child_error}

            # Normalize vector fields within create_child (handles single object or array)
            def normalize_child_params(child: Any, index: int | None = None) -> tuple[dict | None, str | None]:
                prefix = f"create_child[{index}]" if index is not None else "create_child"
                if not isinstance(child, dict):
                    return None, f"{prefix} must be a dict with child properties (name, primitive_type, position, etc.), got {type(child).__name__}"
                child_params = dict(child)
                vec_params, vec_error = normalize_param_map(
                    child_params,
                    [
                        rule_vector3("position", param_name=f"{prefix}.position"),
                        rule_vector3("rotation", param_name=f"{prefix}.rotation"),
                        rule_vector3("scale", param_name=f"{prefix}.scale"),
                    ],
                )
                if vec_error:
                    return None, vec_error
                if vec_params:
                    child_params.update(vec_params)
                return child_params, None

            if isinstance(create_child, list):
                # Array of children
                normalized_children = []
                for i, child in enumerate(create_child):
                    child_params, err = normalize_child_params(child, i)
                    if err:
                        return {"success": False, "message": err}
                    normalized_children.append(child_params)
                params["createChild"] = normalized_children
            else:
                # Single child object
                child_params, err = normalize_child_params(create_child)
                if err:
                    return {"success": False, "message": err}
                params["createChild"] = child_params

        # Send command to Unity
        response = await send_with_unity_instance(
            async_send_command_with_retry, unity_instance, "manage_prefabs", params
        )

        # Return Unity response directly; ensure success field exists
        # Handle MCPResponse objects (returned on error) by converting to dict
        if hasattr(response, 'model_dump'):
            return response.model_dump()
        if isinstance(response, dict):
            if "success" not in response:
                response["success"] = False
            return response
        return {
            "success": False,
            "message": f"Unexpected response type: {type(response).__name__}"
        }

    except TimeoutError:
        return {
            "success": False,
            "message": "Unity connection timeout. Please check if Unity is running and responsive."
        }
    except Exception as exc:
        return {
            "success": False,
            "message": f"Error managing prefabs: {exc}"
        }
