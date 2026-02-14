from pathlib import Path


TOOLS_DIR = Path(__file__).resolve().parents[1] / "src" / "services" / "tools"

# Tools migrated to the unified normalization layer.
MIGRATED_TOOL_FILES = [
    "manage_prefabs.py",
    "manage_material.py",
    "manage_components.py",
    "manage_asset.py",
    "manage_scene.py",
    "read_console.py",
    "find_gameobjects.py",
    "manage_editor.py",
    "execute_custom_tool.py",
    "manage_scriptable_object.py",
    "manage_texture.py",
    "script_apply_edits.py",
    "manage_animation.py",
    "manage_vfx.py",
    "batch_execute.py",
    "manage_gameobject.py",
]

REQUIRED_ENTRYPOINT_TOKENS = (
    "normalize_param_map(",
    "normalize_object(",
    "normalize_json_list(",
    "normalize_object_or_list(",
)

FORBIDDEN_LEGACY_HELPERS = (
    "parse_json_payload(",
    "coerce_bool(",
    "coerce_int(",
    "coerce_float(",
)

FORBIDDEN_INLINE_PLACEHOLDER_CHECKS = (
    "[object Object]",
    "undefined",
)

# Texture keeps domain-specific normalization helpers by design.
LEGACY_HELPER_ALLOWLIST = {"manage_texture.py"}


def _tool_text(file_name: str) -> str:
    return (TOOLS_DIR / file_name).read_text(encoding="utf-8")


def test_migrated_tool_files_exist() -> None:
    for file_name in MIGRATED_TOOL_FILES:
        assert (TOOLS_DIR / file_name).exists(), f"Missing migrated tool file: {file_name}"


def test_migrated_tools_use_normalization_entrypoints() -> None:
    for file_name in MIGRATED_TOOL_FILES:
        text = _tool_text(file_name)
        assert any(token in text for token in REQUIRED_ENTRYPOINT_TOKENS), (
            f"{file_name} must use unified normalization entrypoints from utils.py "
            "(normalize_param_map/normalize_object/normalize_json_list/normalize_object_or_list)."
        )


def test_migrated_tools_do_not_use_legacy_helpers_except_allowlist() -> None:
    for file_name in MIGRATED_TOOL_FILES:
        if file_name in LEGACY_HELPER_ALLOWLIST:
            continue
        text = _tool_text(file_name)
        for token in FORBIDDEN_LEGACY_HELPERS:
            assert token not in text, (
                f"{file_name} should not call legacy helper {token}. "
                "Use normalize_param_map + rule_* or normalize_object/normalize_json_list instead."
            )


def test_migrated_tools_do_not_inline_placeholder_checks_except_allowlist() -> None:
    for file_name in MIGRATED_TOOL_FILES:
        if file_name in LEGACY_HELPER_ALLOWLIST:
            continue
        text = _tool_text(file_name)
        for placeholder in FORBIDDEN_INLINE_PLACEHOLDER_CHECKS:
            assert placeholder not in text, (
                f"{file_name} should not inline placeholder check '{placeholder}'. "
                "Use utils.py placeholder validation helpers."
            )
