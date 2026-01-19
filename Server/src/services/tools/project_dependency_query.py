"""Project dependency query tools - search and analyze asset dependencies.

This module provides tools for querying asset dependencies using a cached index
for fast lookups. It supports finding what assets depend on and what depends
on them with AI-optimized contextual queries.

Tools:
- search_asset_dependency: Contextual search for asset dependencies with token optimization
- get_index_stats: Global index statistics and hot asset identification
- search_assets_by_name: Search for assets by name pattern
- get_assets_by_type: Get all assets of a specific type
"""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, coerce_int, coerce_str
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


QueryStrategy = Literal["balanced", "deep", "slim"]


@mcp_for_unity_tool(
    description=(
        "Searches asset dependencies with contextual filtering for AI. "
        "Use focus parameter for minimal token usage. "
        "Returns focused asset + direct dependencies + reverse dependents. "
        "Includes global statistics to prevent blind spots."
    ),
)
async def search_asset_dependency(
    ctx: Context,
    focus_path: Annotated[str, "The center asset to query around (e.g., 'Assets/Prefabs/MyPrefab.prefab')."],
    max_depth: Annotated[int, "Depth of dependency graph (1=direct only, 2=with indirect). Default: 1."] | None = None,
    include_code: Annotated[bool, "Include code snippets for scripts (lazy-extracted). Default: false."] | None = None,
    strategy: Annotated[QueryStrategy, "Query strategy: balanced (filter raw resources), deep (include all), slim (paths only). Default: balanced."] | None = None,
    include_dependents: Annotated[bool, "Whether to include reverse dependents (assets that depend on this). Default: true."] | None = None,
    auto_generate_index: Annotated[bool, "Auto-generate index if missing. Default: true."] | None = None,
    # Legacy parameter alias for backward compatibility
    asset_path: Annotated[str, "Legacy parameter: maps to focus_path"] | None = None,
) -> dict[str, Any]:
    """Search for asset dependencies using AI-optimized contextual queries.

    This tool provides token-efficient dependency queries with three key innovations:

    1. Three-Tier Semantic Pyramid:
       - Level 1 (Scripts/Prefabs): Full details with weighted scores
       - Level 2 (Structural): Included by default in balanced mode
       - Level 3 (Raw Resources): Filtered in balanced/slim modes

    2. Weighted Reference Scores:
       - Prevents "false hotspots" like Default-Material
       - Scripts/Prefabs: 1.0x weight, Textures/Materials: 0.1x weight

    3. Global Statistics:
       - Included in every response to prevent AI blind spots
       - Shows total assets, top hot assets, depth hints

    Args:
        focus_path: The center asset to query around (primary parameter)
        max_depth: Depth of dependency graph (1=direct, 2=with indirect)
        include_code: Include code snippets for scripts (lazy-extracted and cached)
        strategy: Query strategy - balanced/deep/slim
        include_dependents: Include reverse dependencies (what depends on this)
        auto_generate_index: Auto-generate index if missing

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with contextual query results
            - focus: dict - Focused asset summary (path, type, semantic_level, reference_count)
            - dependencies: list[dict] - Direct dependencies of focus
            - dependents: list[dict] - Assets that depend on focus (reverse deps)
            - global_stats: dict - Prevents AI blind spots
                - total_assets: int - Total assets in index
                - top_hot_assets: list - Hot assets with weighted scores
                - depth_hint: str - Hint about data completeness
            - metadata: dict - Query metadata
                - focus_path: str - Queried asset path
                - max_depth: int - Depth used
                - total_nodes: int - Nodes returned
                - strategy: str - Strategy applied
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        # Support both focus_path and legacy asset_path parameter
        effective_focus_path = focus_path or asset_path
        if not effective_focus_path:
            return {"success": False, "message": "focus_path parameter is required."}

        coerced_max_depth = coerce_int(max_depth, default=1)
        coerced_include_code = coerce_bool(include_code, default=False)
        coerced_strategy = coerce_str(strategy, default="balanced")
        coerced_include_dependents = coerce_bool(include_dependents, default=True)
        coerced_auto_generate = coerce_bool(auto_generate_index, default=True)

        # Validate strategy
        if coerced_strategy not in ("balanced", "deep", "slim"):
            coerced_strategy = "balanced"

        params: dict[str, Any] = {
            "focus_path": effective_focus_path,
            "max_depth": coerced_max_depth,
            "include_code": coerced_include_code,
            "strategy": coerced_strategy,
            "include_dependents": coerced_include_dependents,
            "auto_generate_index": coerced_auto_generate,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "search_asset_dependency",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Dependency search complete."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error searching dependencies: {str(e)}"}


@mcp_for_unity_tool(
    description=(
        "Returns global index statistics for AI context awareness. "
        "Use this BEFORE deep queries to understand project scope. "
        "Identifies hot assets and potential refactoring risks."
    ),
)
async def get_index_stats(
    ctx: Context,
    top_n: Annotated[int, "Maximum number of hot assets to return. Default: 10."] | None = None,
    semantic_level: Annotated[str, "Filter by semantic level (1=scripts, 2=prefabs, 3=resources). Default: all."] | None = None,
) -> dict[str, Any]:
    """Get global index statistics and identify hot assets.

    Use this tool BEFORE making deep queries to understand the project scope.
    It helps identify critical assets that would be risky to modify.

    The weighted_score prevents "false hotspots" - shared resources like
    Default-Material appear with lower scores than core logic assets.

    Args:
        top_n: Maximum number of hot assets to return (default: 10)
        semantic_level: Optional filter by semantic level
            - "1" or "scripts": Core semantic (Scripts, ScriptableObjects)
            - "2" or "prefabs": Structural (Prefabs, Scenes)
            - "3" or "resources": Raw resources (Textures, Materials, etc.)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with index statistics
            - total_assets: int - Total number of assets in index
            - top_hot_assets: list[dict] - Hot assets sorted by weighted score
                - path: str - Asset path
                - type: str - Asset type
                - reference_count: int - Raw number of references
                - weighted_score: float - Weighted score (ref_count × weight)
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_top_n = coerce_int(top_n, default=10)
        coerced_semantic_level = coerce_str(semantic_level, default="")

        params: dict[str, Any] = {
            "top_n": coerced_top_n,
            "semantic_level": coerced_semantic_level,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_index_stats",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Index stats retrieved."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting index stats: {str(e)}"}


@mcp_for_unity_tool(
    description="Searches for assets by name pattern using the cached dependency index.",
)
async def search_assets_by_name(
    ctx: Context,
    name_pattern: Annotated[str, "Name pattern to search for (supports partial matching)."],
    max_results: Annotated[int, "Maximum number of results to return."] | None = None,
) -> dict[str, Any]:
    """Search for assets by name pattern using the cached index.

    Performs fast name-based searches on the dependency index. Supports
    partial matching to find assets with similar names.

    Args:
        name_pattern: The name pattern to search for (e.g., 'Player', 'UI_', 'Button')
        max_results: Maximum number of results to return (default: 20)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with search results
            - name_pattern: str - The search pattern used
            - total_found: int - Number of matching assets
            - assets: list[dict] - List of matching assets with path, type, guid
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_max_results = coerce_int(max_results, default=20)

        params: dict[str, Any] = {
            "name_pattern": name_pattern,
            "max_results": coerced_max_results,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "search_assets_by_name",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Asset search complete."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error searching assets: {str(e)}"}


@mcp_for_unity_tool(
    description="Returns all assets of a specific type from the cached dependency index.",
)
async def get_assets_by_type(
    ctx: Context,
    asset_type: Annotated[str, "Asset type to filter (e.g., Prefab, Material, Texture, ScriptableObject)."],
) -> dict[str, Any]:
    """Get all assets of a specific type from the cached index.

    Retrieves a complete list of assets matching the specified type.
    Common types include: Prefab, Material, Texture, Mesh, Script, Scene, Shader.

    Args:
        asset_type: The type of asset to filter (case-insensitive)

    Returns:
        Dict with keys:
        - success: bool - Operation result
        - message: str - Status message
        - data: dict with type results
            - asset_type: str - The type queried
            - total_found: int - Number of assets of this type
            - paths: list[str] - List of asset paths
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        params: dict[str, Any] = {
            "asset_type": asset_type,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "get_assets_by_type",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", f"Found assets of type '{asset_type}'."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error getting assets by type: {str(e)}"}
