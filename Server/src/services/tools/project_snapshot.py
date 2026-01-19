"""Project snapshot generation tool - generates AI-friendly project overview.

This module provides the main project_snapshot tool for generating comprehensive
project snapshots that help AI understand Unity project structure.

Tools:
- project_snapshot: Generate/read/check AI-friendly project snapshot
"""

from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services.registry import mcp_for_unity_tool
from services.tools import get_unity_instance_from_context
from services.tools.utils import coerce_bool, coerce_int
from transport.unity_transport import send_with_unity_instance
from transport.legacy.unity_connection import async_send_command_with_retry


@mcp_for_unity_tool(
    description="Generates/reads AI-friendly project snapshot. Auto-generates after compilation. Use action='read' to quickly access existing snapshots, action='status' to check freshness.",
    annotations=ToolAnnotations(
        title="Project Snapshot",
        experimental=True,
    ),
)
async def project_snapshot(
    ctx: Context,
    action: Annotated[Literal["generate", "read", "status"], "Action: 'generate' (default), 'read' (existing snapshot), 'status' (check freshness)"] = "generate",
    # Generation options (only used when action='generate')
    include_packages: Annotated[bool, "If true, include Packages folder in analysis."] | None = None,
    max_depth: Annotated[int, "Maximum directory depth to traverse (default: 4)."] | None = None,
    include_dependencies: Annotated[bool, "If true, include detailed dependency analysis."] | None = None,
    include_data_schemas: Annotated[bool, "If true, include ScriptableObject and JSON schema info."] | None = None,
    output_path: Annotated[str, "Path where snapshot markdown will be saved."] | None = None,
    use_cache: Annotated[bool, "If true, use cached snapshot when valid."] | None = None,
    force_regenerate: Annotated[bool, "If true, force full regeneration ignoring cache."] | None = None,
    separate_dependencies_file: Annotated[bool, "If true, generate dependencies as separate file."] | None = None,
    generate_dependencies_file: Annotated[bool, "Alias for separate_dependencies_file."] | None = None,
    dependencies_output_path: Annotated[str, "Path for dependencies file."] | None = None,
    generate_index: Annotated[bool, "If true, generate dependency index for fast queries."] | None = None,
    settings_asset_path: Annotated[str, "Optional path to ProjectSnapshotSettings asset."] | None = None,
    # Limit options
    max_prefabs_to_analyze: Annotated[int, "Maximum prefabs to analyze for dependencies."] | None = None,
    max_core_prefabs: Annotated[int, "Maximum core prefabs to identify."] | None = None,
    max_dependencies_per_prefab: Annotated[int, "Max dependencies per prefab to list."] | None = None,
    max_manager_classes: Annotated[int, "Maximum manager classes to list."] | None = None,
    max_scriptable_objects: Annotated[int, "Maximum ScriptableObject types to return."] | None = None,
    max_files_to_scan: Annotated[int, "Maximum files to scan for data schemas."] | None = None,
    max_json_examples: Annotated[int, "Maximum JSON examples to return."] | None = None,
    # Smart folding options
    enable_smart_folding: Annotated[bool, "Enable smart directory folding (resource-heavy folders collapse)."] | None = None,
    folding_threshold: Annotated[int, "File count threshold for folder folding (default: 30)."] | None = None,
    # Circuit breaker options
    max_snapshot_tokens: Annotated[int, "Token budget for main snapshot (default: 5000)."] | None = None,
    max_dependency_tokens: Annotated[int, "Token budget for dependencies file (default: 8000)."] | None = None,
    max_prefabs_in_snapshot: Annotated[int, "Maximum prefabs to include in snapshot (default: 200)."] | None = None,
    # Priority settings
    core_naming_keywords: Annotated[list[str], "Keywords for identifying important prefabs."] | None = None,
    top_dependencies_to_show: Annotated[int, "Top N dependencies to show per prefab (default: 3)."] | None = None,
    # Read/Status options
    snapshot_path: Annotated[str, "Path to snapshot file (for read/status actions)."] | None = None,
) -> dict[str, Any]:
    """Generate, read, or check status of an AI-friendly snapshot of the Unity project.

    Auto-Generation:
        Snapshots are now automatically generated after script compilation.
        Use action='read' to access the auto-generated snapshot without regenerating.
        Use action='status' to check if the snapshot is fresh or needs regeneration.

    Actions:
        - generate: Force generate a new snapshot (or use cached if valid)
        - read: Read existing snapshot content (recommended for AI)
        - status: Check snapshot freshness and whether regeneration is needed

    Generation Args (only used when action='generate'):
        include_packages: Include Packages folder in analysis (default: false)
        max_depth: Maximum directory depth (default: 4)
        include_dependencies: Include detailed dependency analysis (default: true)
        include_data_schemas: Include data schema info (default: true)
        output_path: Output file path (default: "Project_Snapshot.md")
        use_cache: Use cached snapshot if valid (default: true)
        force_regenerate: Force full regeneration (default: false)
        separate_dependencies_file: Generate dependencies as separate file (default: true)
        generate_dependencies_file: Alias for separate_dependencies_file
        dependencies_output_path: Path for dependencies file (default: "Asset_Dependencies.md")
        generate_index: Generate dependency index for fast queries (default: true)
        settings_asset_path: Optional ProjectSnapshotSettings asset path
        max_prefabs_to_analyze: Max prefabs to analyze (default: 50)
        max_core_prefabs: Max core prefabs to identify (default: 20)
        max_dependencies_per_prefab: Max dependencies per prefab (default: 50)
        max_manager_classes: Max manager classes to list (default: 20)
        max_scriptable_objects: Max ScriptableObject types (default: 10)
        max_files_to_scan: Max files to scan (default: 30)
        max_json_examples: Max JSON examples (default: 3)
        enable_smart_folding: Enable smart directory folding (default: true)
        folding_threshold: File count threshold for folder folding (default: 30)
        max_snapshot_tokens: Token budget for main snapshot (default: 5000)
        max_dependency_tokens: Token budget for dependencies file (default: 8000)
        max_prefabs_in_snapshot: Maximum prefabs in snapshot (default: 200)
        core_naming_keywords: Keywords for identifying important prefabs
        top_dependencies_to_show: Top N dependencies per prefab (default: 3)

    Read/Status Args:
        snapshot_path: Path to snapshot file (default: "Project_Snapshot.md")

    Returns:
        For action='generate':
            - success: bool - Operation result
            - message: str - Status message
            - data: dict with snapshot results
                - output_path: str - Path to generated snapshot
                - word_count: int - Word count of snapshot
                - generation_time_ms: int - Generation time in milliseconds
                - from_cache: bool - True if loaded from cache
                - dependencies_path: str - Path to dependencies file (if generated)
                - dependencies_word_count: int - Word count of dependencies (if generated)
                - index_generated: bool - True if index was generated

        For action='read':
            - success: bool - Operation result
            - message: str - Status message
            - data: dict with read results
                - content: str - Full snapshot markdown content
                - path: str - Path to snapshot file
                - exists: bool - True if snapshot exists
                - last_generated: str - ISO timestamp of last generation
                - age_minutes: float - Age of snapshot in minutes
                - is_dirty: bool - True if project has changed since snapshot

        For action='status':
            - success: bool - Operation result
            - message: str - Status message
            - data: dict with status info
                - exists: bool - True if snapshot exists
                - dependencies_exist: bool - True if dependencies file exists
                - last_generated: str - ISO timestamp of last generation
                - age_minutes: float - Age of snapshot in minutes
                - is_dirty: bool - True if project has changed since snapshot
                - recommendation: str - Suggested action
    """
    unity_instance = get_unity_instance_from_context(ctx)

    try:
        # Build base params
        params: dict[str, Any] = {"action": action}

        # Add snapshot_path for read/status actions
        if snapshot_path:
            params["snapshot_path"] = snapshot_path

        # For generate action, add all generation options
        if action == "generate":
            # Coerce parameters with defaults (must match C# SnapshotOptions defaults)
            coerced_include_packages = coerce_bool(include_packages, default=False)
            coerced_max_depth = coerce_int(max_depth, default=4)
            coerced_include_deps = coerce_bool(include_dependencies, default=True)
            coerced_include_schemas = coerce_bool(include_data_schemas, default=True)
            coerced_use_cache = coerce_bool(use_cache, default=True)
            coerced_force_regenerate = coerce_bool(force_regenerate, default=False)
            coerced_separate_deps = coerce_bool(separate_dependencies_file, default=True)
            if generate_dependencies_file is not None:
                coerced_separate_deps = coerce_bool(generate_dependencies_file, default=False)
            coerced_generate_index = coerce_bool(generate_index, default=True)

            # Limit options
            coerced_max_prefabs = coerce_int(max_prefabs_to_analyze, default=50)
            coerced_max_core = coerce_int(max_core_prefabs, default=20)
            coerced_max_deps = coerce_int(max_dependencies_per_prefab, default=50)
            coerced_max_managers = coerce_int(max_manager_classes, default=20)
            coerced_max_so = coerce_int(max_scriptable_objects, default=10)
            coerced_max_files = coerce_int(max_files_to_scan, default=30)
            coerced_max_json = coerce_int(max_json_examples, default=3)

            # Smart folding and circuit breaker options
            coerced_enable_smart_folding = coerce_bool(enable_smart_folding, default=True)
            coerced_folding_threshold = coerce_int(folding_threshold, default=30)
            coerced_max_snapshot_tokens = coerce_int(max_snapshot_tokens, default=5000)
            coerced_max_dependency_tokens = coerce_int(max_dependency_tokens, default=8000)
            coerced_max_prefabs_in_snapshot = coerce_int(max_prefabs_in_snapshot, default=200)
            coerced_top_deps = coerce_int(top_dependencies_to_show, default=3)

            # Build parameters dict
            params["include_packages"] = coerced_include_packages
            params["max_depth"] = coerced_max_depth
            params["include_dependencies"] = coerced_include_deps
            params["include_data_schemas"] = coerced_include_schemas
            if output_path:
                params["output_path"] = output_path
            else:
                params["output_path"] = "Project_Snapshot.md"
            params["use_cache"] = coerced_use_cache
            params["force_regenerate"] = coerced_force_regenerate
            params["separate_dependencies_file"] = coerced_separate_deps
            if dependencies_output_path:
                params["dependencies_output_path"] = dependencies_output_path
            else:
                params["dependencies_output_path"] = "Asset_Dependencies.md"
            params["generate_index"] = coerced_generate_index
            if settings_asset_path:
                params["settings_asset_path"] = settings_asset_path

            # Limit options
            if max_prefabs_to_analyze is not None:
                params["max_prefabs_to_analyze"] = coerced_max_prefabs
            if max_core_prefabs is not None:
                params["max_core_prefabs"] = coerced_max_core
            if max_dependencies_per_prefab is not None:
                params["max_dependencies_per_prefab"] = coerced_max_deps
            if max_manager_classes is not None:
                params["max_manager_classes"] = coerced_max_managers
            if max_scriptable_objects is not None:
                params["max_scriptable_objects"] = coerced_max_so
            if max_files_to_scan is not None:
                params["max_files_to_scan"] = coerced_max_files
            if max_json_examples is not None:
                params["max_json_examples"] = coerced_max_json

            # Smart folding and circuit breaker options
            if enable_smart_folding is not None:
                params["enable_smart_folding"] = coerced_enable_smart_folding
            if folding_threshold is not None:
                params["folding_threshold"] = coerced_folding_threshold
            if max_snapshot_tokens is not None:
                params["max_snapshot_tokens"] = coerced_max_snapshot_tokens
            if max_dependency_tokens is not None:
                params["max_dependency_tokens"] = coerced_max_dependency_tokens
            if max_prefabs_in_snapshot is not None:
                params["max_prefabs_in_snapshot"] = coerced_max_prefabs_in_snapshot
            if core_naming_keywords is not None:
                params["core_naming_keywords"] = core_naming_keywords
            if top_dependencies_to_show is not None:
                params["top_dependencies_to_show"] = coerced_top_deps

        response = await send_with_unity_instance(
            async_send_command_with_retry,
            unity_instance,
            "project_snapshot",
            params,
        )

        if isinstance(response, dict) and response.get("success"):
            return {
                "success": True,
                "message": response.get("message", "Project snapshot operation completed."),
                "data": response.get("data"),
            }
        return response if isinstance(response, dict) else {"success": False, "message": str(response)}

    except Exception as e:
        return {"success": False, "message": f"Python error in snapshot operation: {str(e)}"}
