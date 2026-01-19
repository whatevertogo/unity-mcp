"""Project query tools - architecture, directory structure, and data schema analysis.

This module provides tools for understanding Unity project structure without
generating a full snapshot. It answers questions about architecture,
folder organization, and data structures.

Tools:
- get_project_architecture: Returns project architecture analysis
- get_project_directory: Returns project directory structure as a tree
- get_data_schema: Returns data schema information including ScriptableObjects
"""

from typing import Annotated, Any

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Returns project architecture analysis including architecture type, entry points, loading strategy, and manager classes. Use when you need to understand the project structure without generating a full snapshot.",
)
async def get_project_architecture(
    ctx: Context,
    include_entry_points: Annotated[bool, "If true, include detected entry point scripts."] | None = None,
    include_managers: Annotated[bool, "If true, include manager/controller classes."] | None = None,
    max_managers: Annotated[int, "Maximum number of manager classes to return."] | None = None,
) -> dict[str, Any]:
    """Analyze and return the project's architecture type and organization.

    This tool detects:
    - Architecture type (MVC, ECS, component-based, etc.)
    - Loading strategy (scene-based, addressables, resources, etc.)
    - Entry point scripts (GameManagers, Bootstraps, etc.)
    - Manager/Controller classes

    Args:
        include_entry_points: Include detected entry point scripts (default: true)
        include_managers: Include manager/controller classes (default: true)
        max_managers: Maximum number of managers to return (default: 20)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with architecture analysis
            - architecture_type: str - Detected architecture pattern
            - architecture_confidence: str - Confidence in detection
            - loading_strategy: str - Detected loading approach
            - loading_indicators: list[str] - Evidence for loading strategy
            - entry_points: list[dict] - Entry point scripts (if requested)
            - manager_classes: list[str] - Manager class names (if requested)
            - manager_count: int - Total manager count
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_include_entry = coerce_bool(include_entry_points, default=True)
        coerced_include_managers = coerce_bool(include_managers, default=True)
        coerced_max_managers = coerce_int(max_managers, default=20)

        params: dict[str, Any] = {
            "include_entry_points": coerced_include_entry,
            "include_managers": coerced_include_managers,
            "max_managers": coerced_max_managers,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_project_architecture",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Architecture analysis complete."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error analyzing architecture: {str(e)}"}


@mcp_for_unity_tool(
    description="Returns project directory structure as a tree. Use when you need to explore the project folder structure without generating a full snapshot.",
)
async def get_project_directory(
    ctx: Context,
    root_path: Annotated[str, "Root path to start directory tree (default: 'Assets')."] | None = None,
    max_depth: Annotated[int, "Maximum depth to traverse (default: 4)."] | None = None,
    include_file_counts: Annotated[bool, "If true, include file and subdirectory counts."] | None = None,
    include_packages: Annotated[bool, "If true, include Packages folder in traversal."] | None = None,
) -> dict[str, Any]:
    """Get the project directory structure as a hierarchical tree.

    Returns a tree representation of the project folder structure with
    optional file counts and smart folder commenting.

    Args:
        root_path: Starting path for directory traversal (default: "Assets")
        max_depth: Maximum depth to traverse (default: 4)
        include_file_counts: Include file/subdirectory counts per folder (default: true)
        include_packages: Include Packages folder (default: false)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with directory tree
            - root_path: str - The root path used
            - max_depth: int - The max depth used
            - directory_tree: list[dict] - Hierarchical directory structure
            - stats: dict - Overall statistics
                - total_directories: int
                - total_files: int
                - file_types: dict - File extension counts
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_max_depth = coerce_int(max_depth, default=4)
        coerced_include_counts = coerce_bool(include_file_counts, default=True)
        coerced_include_packages = coerce_bool(include_packages, default=False)

        params: dict[str, Any] = {
            "root_path": root_path or "Assets",
            "max_depth": coerced_max_depth,
            "include_file_counts": coerced_include_counts,
            "include_packages": coerced_include_packages,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_project_directory",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Directory structure generated."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting directory structure: {str(e)}"}


@mcp_for_unity_tool(
    description="Returns data schema information including ScriptableObject definitions and JSON data examples. Use when you need to understand the data structures in the project.",
)
async def get_data_schema(
    ctx: Context,
    include_scriptable_objects: Annotated[bool, "If true, include ScriptableObject definitions."] | None = None,
    include_json_examples: Annotated[bool, "If true, include JSON data file examples."] | None = None,
    max_scriptable_objects: Annotated[int, "Maximum ScriptableObject types to return."] | None = None,
    max_json_examples: Annotated[int, "Maximum JSON examples to return."] | None = None,
) -> dict[str, Any]:
    """Get information about data structures in the project.

    Analyzes and returns:
    - ScriptableObject class definitions with code snippets
    - JSON data file examples

    This is useful for understanding the data model and configuration
    patterns used in the project.

    Args:
        include_scriptable_objects: Include ScriptableObject definitions (default: true)
        include_json_examples: Include JSON data file examples (default: true)
        max_scriptable_objects: Max ScriptableObject types to return (default: 10)
        max_json_examples: Max JSON examples to return (default: 3)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with schema information
            - scriptable_objects: list[dict] - ScriptableObject type info
                - path: str - File path
                - class_name: str - Class name
                - code_snippet: str - Relevant code excerpt
            - scriptable_object_count: int - Total found
            - json_examples: list[dict] - JSON file examples
                - path: str - File path
                - content: str - File content
            - json_example_count: int - Total JSON files
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_include_so = coerce_bool(include_scriptable_objects, default=True)
        coerced_include_json = coerce_bool(include_json_examples, default=True)
        coerced_max_so = coerce_int(max_scriptable_objects, default=10)
        coerced_max_json = coerce_int(max_json_examples, default=3)

        params: dict[str, Any] = {
            "include_scriptable_objects": coerced_include_so,
            "include_json_examples": coerced_include_json,
            "max_scriptable_objects": coerced_max_so,
            "max_json_examples": coerced_max_json,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_data_schema",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Data schema retrieved."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting data schema: {str(e)}"}
