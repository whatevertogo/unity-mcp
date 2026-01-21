"""Project dependency inspection tools.

This module provides tools for inspecting asset dependencies using a cached index
for fast lookups. Supports both focused analysis (specific asset) and global mode
(hot assets & circular dependency warnings).

Tools:
- inspect_dependency: Unified tool for dependency inspection
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
        "Inspect asset dependencies. "
        "Use focus_path for specific asset analysis, or omit for global mode (hot assets + circular dependency warnings). "
        "Essential for refactoring and impact analysis."
    ),
    annotations=ToolAnnotations(title="Inspect Dependencies"),
)
async def inspect_dependency(
    ctx: Context,
    focus_path: Annotated[str, "Asset path to analyze (e.g., 'Assets/Prefabs/MyPrefab.prefab'). If empty, returns global stats (hot assets + circular dependencies)."] | None = None,
    max_depth: Annotated[int, "Depth of dependency graph (1=direct only, 2=with indirect). Default: 1."] | None = None,
    include_code: Annotated[bool, "Include code snippets for scripts. Default: false."] | None = None,
    strategy: Annotated[QueryStrategy, "Query strategy: balanced/deep/slim. Default: balanced."] | None = None,
    include_dependents: Annotated[bool, "Include reverse dependents. Default: true."] | None = None,
    auto_generate_index: Annotated[bool, "Auto-generate index if missing. Default: true."] | None = None,
    # For global mode
    top_n: Annotated[int, "Max hot assets to return in global mode. Default: 10."] | None = None,
) -> dict[str, Any]:
    """Inspect asset dependencies with AI-optimized contextual queries.

    Supports two modes:

    1. Focus Mode (focus_path provided):
       Analyzes dependencies for a specific asset
       Returns: focus asset + dependencies + dependents + global stats

    2. Global Mode (focus_path empty/omitted):
       Returns project-level statistics including:
       - Hot assets (most referenced)
       - Circular dependency warnings
       - Total asset count

    Args:
        focus_path: Asset path to analyze (omit for global mode)
        max_depth: Dependency graph depth (1=direct, 2=with indirect)
        include_code: Include code snippets for scripts
        strategy: Query strategy - balanced/deep/slim
        include_dependents: Include reverse dependencies
        auto_generate_index: Auto-generate index if missing
        top_n: Max hot assets for global mode

    Returns:
        Focus mode:
        - focus: dict - Focused asset summary
        - dependencies: list[dict] - Direct dependencies
        - dependents: list[dict] - Reverse dependencies
        - global_stats: dict - Project-wide statistics

        Global mode:
        - total_assets: int - Total assets in index
        - hot_assets: list[dict] - Top hot assets
        - circular_dependencies: list[list[str]] - Circular dependency chains
        - warnings: list[str] - Warnings about critical assets/cycles
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        coerced_max_depth = coerce_int(max_depth, default=1)
        coerced_include_code = coerce_bool(include_code, default=False)
        coerced_strategy = coerce_str(strategy, default="balanced")
        coerced_include_dependents = coerce_bool(include_dependents, default=True)
        coerced_auto_generate = coerce_bool(auto_generate_index, default=True)
        coerced_top_n = coerce_int(top_n, default=10)

        # Validate strategy
        if coerced_strategy not in ("balanced", "deep", "slim"):
            coerced_strategy = "balanced"

        params: dict[str, Any] = {
            "focus_path": focus_path or "",
            "max_depth": coerced_max_depth,
            "include_code": coerced_include_code,
            "strategy": coerced_strategy,
            "include_dependents": coerced_include_dependents,
            "auto_generate_index": coerced_auto_generate,
            "top_n": coerced_top_n,
        }

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "inspect_dependency",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Dependency inspection complete."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Error inspecting dependencies: {str(e)}"}
