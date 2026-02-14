"""
Shared helper utilities for MCP server tools.

Architecture:
    - Rule builders (rule_*): Create declarative normalization rules
    - Core normalization (normalize_*): Type-specific normalization logic
    - Utilities (_*): Internal helpers for implementation

Normalization policy for tool authors:
1. New/updated tools should normalize parameters through `normalize_param_map` + `rule_*`.
2. For non-map payloads, use `normalize_object`, `normalize_json_list`, or `normalize_object_or_list`.
3. Do not hand-roll placeholder checks (`[object Object]`, `undefined`) in tool files.
4. `coerce_*` and `parse_json_payload` are legacy compatibility helpers. Prefer rule-based entrypoints.
5. For async tools, consider using `@with_normalized_params` decorator to reduce boilerplate.
"""

from __future__ import annotations

import json
import math
import warnings
from dataclasses import dataclass
from typing import Any, Callable, Mapping, Sequence

_TRUTHY = {"true", "1", "yes", "on"}
_FALSY = {"false", "0", "no", "off"}
_INVALID_SERIALIZED_VALUES = {"[object Object]", "undefined", "null", ""}
_INVALID_OBJECT_STRING_PLACEHOLDERS = {"[object Object]", "undefined"}
Normalizer = Callable[[Any], tuple[Any, str | None]]

_LEGACY_HELPER_WARNING = (
    "{name} is a legacy compatibility helper. "
    "Use normalize_param_map + rule_* (or normalize_object/normalize_json_list) in tool code."
)


def _warn_legacy_helper(name: str) -> None:
    warnings.warn(
        _LEGACY_HELPER_WARNING.format(name=name),
        DeprecationWarning,
        stacklevel=3,
    )


def is_invalid_serialized_value(value: Any) -> bool:
    """Return True if the input is a known placeholder from broken client serialization."""
    return isinstance(value, str) and value.strip() in _INVALID_SERIALIZED_VALUES


def is_invalid_object_string_placeholder(value: Any) -> bool:
    """Return True for object-placeholder strings commonly produced by broken serialization."""
    return isinstance(value, str) and value.strip() in _INVALID_OBJECT_STRING_PLACEHOLDERS

@dataclass(frozen=True, slots=True)
class ParamRule:
    """Declarative normalization rule for a single tool parameter."""
    input_key: str
    normalizer: Normalizer
    output_key: str | None = None
    include_none: bool = False


def rule_vector3(input_key: str, output_key: str | None = None, param_name: str | None = None) -> ParamRule:
    """Create a vector3 normalization rule."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_vector3(value, label),
        output_key=output_key,
    )


def rule_string_list(input_key: str, output_key: str | None = None, param_name: str | None = None) -> ParamRule:
    """Create a string list normalization rule."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_string_list(value, label),
        output_key=output_key,
    )


def rule_object(input_key: str, output_key: str | None = None, param_name: str | None = None) -> ParamRule:
    """Create a dict/json-object normalization rule."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_object(value, label),
        output_key=output_key,
    )


def rule_bool(
    input_key: str,
    output_key: str | None = None,
    default: bool | None = None,
    include_none: bool = False,
) -> ParamRule:
    """Create a bool coercion rule."""
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: (coerce_bool(value, default=default, _warn_legacy=False), None),
        output_key=output_key,
        include_none=include_none,
    )


def rule_int(
    input_key: str,
    output_key: str | None = None,
    default: int | None = None,
    include_none: bool = False,
) -> ParamRule:
    """Create an int coercion rule."""
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: (coerce_int(value, default=default, _warn_legacy=False), None),
        output_key=output_key,
        include_none=include_none,
    )


def rule_float(
    input_key: str,
    output_key: str | None = None,
    default: float | None = None,
    include_none: bool = False,
) -> ParamRule:
    """Create a float coercion rule."""
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: (coerce_float(value, default=default, _warn_legacy=False), None),
        output_key=output_key,
        include_none=include_none,
    )


def rule_json_list(input_key: str, output_key: str | None = None, param_name: str | None = None) -> ParamRule:
    """Create a JSON-list normalization rule."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_json_list(value, label),
        output_key=output_key,
    )


def rule_json_value(
    input_key: str,
    output_key: str | None = None,
    param_name: str | None = None,
    reject_invalid_placeholder: bool = True,
) -> ParamRule:
    """Create a tolerant JSON value parsing rule."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_json_value(
            value,
            label,
            reject_invalid_placeholder=reject_invalid_placeholder,
        ),
        output_key=output_key,
    )


def rule_non_placeholder_string(
    input_key: str,
    output_key: str | None = None,
    param_name: str | None = None,
    allow_non_string: bool = True,
) -> ParamRule:
    """Create a rule that rejects placeholder strings while keeping other values."""
    label = param_name or input_key
    return ParamRule(
        input_key=input_key,
        normalizer=lambda value: normalize_non_placeholder_string(
            value,
            label,
            allow_non_string=allow_non_string,
        ),
        output_key=output_key,
    )


def normalize_param_map(
    values: Mapping[str, Any],
    rules: Sequence[ParamRule],
) -> tuple[dict[str, Any] | None, str | None]:
    """
    Apply a list of normalization rules to a parameter map.

    Returns:
        (normalized_map, error_message)
    """
    normalized: dict[str, Any] = {}
    for rule in rules:
        input_value = values.get(rule.input_key)
        coerced, error = rule.normalizer(input_value)
        if error:
            return None, error

        if coerced is None and not rule.include_none:
            continue

        normalized[rule.output_key or rule.input_key] = coerced
    return normalized, None


def _extract_param_dict(func: Callable, *args: Any, **kwargs: Any) -> dict[str, Any]:
    """Extract parameter names and values from function call."""
    import inspect
    sig = inspect.signature(func)
    bound = sig.bind_partial(*args, **kwargs)
    bound.apply_defaults()
    return dict(bound.arguments)


def with_normalized_params(
    *rules: ParamRule,
    error_response_key: str = "success",
    error_message_key: str = "message",
) -> Callable:
    """
    Decorator that normalizes parameters using rules and returns early on error.

    Usage:
        @with_normalized_params(
            rule_bool("save_as_prefab", output_key="saveAsPrefab"),
            rule_vector3("position"),
        )
        async def manage_asset(ctx, action, save_as_prefab=None, position=None, ...):
            # saveAsPrefab and position are now normalized
            ...

    The decorator injects normalized values back into the function's kwargs.
    If normalization fails, returns an error dict immediately.
    """
    from functools import wraps

    def decorator(func: Callable) -> Callable:
        @wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> dict[str, Any]:
            # Extract all parameters
            param_dict = _extract_param_dict(func, *args, **kwargs)

            # Apply normalization rules
            normalized, error = normalize_param_map(param_dict, rules)
            if error:
                return {error_response_key: False, error_message_key: error}

            # Update kwargs with normalized values
            kwargs.update(normalized)

            return await func(*args, **kwargs)
        return wrapper
    return decorator


def coerce_bool(
    value: Any,
    default: bool | None = None,
    *,
    _warn_legacy: bool = True,
) -> bool | None:
    """Attempt to coerce a loosely-typed value to a boolean."""
    if _warn_legacy:
        _warn_legacy_helper("coerce_bool")
    if value is None:
        return default
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        lowered = value.strip().lower()
        if lowered in _TRUTHY:
            return True
        if lowered in _FALSY:
            return False
        return default
    return bool(value)


def parse_json_payload(value: Any, *, _warn_legacy: bool = True) -> Any:
    """
    Attempt to parse a value that might be a JSON string into its native object.

    This is a tolerant parser used to handle cases where MCP clients or LLMs
    serialize complex objects (lists, dicts) into strings. It also handles
    scalar values like numbers, booleans, and null.

    Args:
        value: The input value (can be str, list, dict, etc.)

    Returns:
        The parsed JSON object/list if the input was a valid JSON string,
        or the original value if parsing failed or wasn't necessary.
    """
    if _warn_legacy:
        _warn_legacy_helper("parse_json_payload")

    if not isinstance(value, str):
        return value

    val_trimmed = value.strip()

    # Fast path: if it doesn't look like JSON structure, return as is
    if not (
        (val_trimmed.startswith("{") and val_trimmed.endswith("}")) or
        (val_trimmed.startswith("[") and val_trimmed.endswith("]")) or
        val_trimmed in ("true", "false", "null") or
        (val_trimmed.replace(".", "", 1).replace("-", "", 1).isdigit())
    ):
        return value

    try:
        return json.loads(value)
    except (json.JSONDecodeError, ValueError):
        # If parsing fails, assume it was meant to be a literal string
        return value


def coerce_int(
    value: Any,
    default: int | None = None,
    *,
    _warn_legacy: bool = True,
) -> int | None:
    """Attempt to coerce a loosely-typed value to an integer."""
    if _warn_legacy:
        _warn_legacy_helper("coerce_int")
    if value is None:
        return default
    try:
        if isinstance(value, bool):
            return default
        if isinstance(value, int):
            return value
        s = str(value).strip()
        if s.lower() in ("", "none", "null"):
            return default
        return int(float(s))
    except Exception:
        return default


def coerce_float(
    value: Any,
    default: float | None = None,
    *,
    _warn_legacy: bool = True,
) -> float | None:
    """Attempt to coerce a loosely-typed value to a float-like number."""
    if _warn_legacy:
        _warn_legacy_helper("coerce_float")
    if value is None:
        return default
    try:
        # Treat booleans as invalid numeric input instead of coercing to 0/1.
        if isinstance(value, bool):
            return default
        if isinstance(value, (int, float)):
            return float(value)
        s = str(value).strip()
        if s.lower() in ("", "none", "null"):
            return default
        return float(s)
    except (TypeError, ValueError):
        return default


def normalize_properties(value: Any) -> tuple[dict[str, Any] | None, str | None]:
    """
    Robustly normalize a properties parameter to a dict.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - dict -> (dict, None)
    - JSON string -> (parsed_dict, None) or (None, error_message)
    - Invalid values -> (None, error_message)

    Returns:
        Tuple of (parsed_dict, error_message). If error_message is set, parsed_dict is None.
    """
    return normalize_object(value, "properties")


def normalize_object(value: Any, param_name: str = "object") -> tuple[dict[str, Any] | None, str | None]:
    """
    Robustly normalize a dict-like parameter from native dict or JSON object string.

    Returns:
        Tuple of (parsed_dict, error_message). If error_message is set, parsed_dict is None.
    """
    if value is None:
        return None, None

    if isinstance(value, dict):
        return value, None

    if isinstance(value, str):
        if is_invalid_serialized_value(value):
            return None, (
                f"{param_name} received invalid value: '{value}'. "
                "Expected a JSON object like {\"key\": value}"
            )

        parsed = parse_json_payload(value, _warn_legacy=False)
        if isinstance(parsed, dict):
            return parsed, None

        return None, (
            f"{param_name} must be a JSON object (dict), "
            f"got string that parsed to {type(parsed).__name__}"
        )

    return None, f"{param_name} must be a dict or JSON string, got {type(value).__name__}"


def normalize_json_list(value: Any, param_name: str = "list") -> tuple[list[Any] | None, str | None]:
    """
    Normalize a list-like parameter from native list/tuple or JSON array string.

    Returns:
        Tuple of (parsed_list, error_message). If error_message is set, parsed_list is None.
    """
    if value is None:
        return None, None

    if isinstance(value, list):
        return value, None

    if isinstance(value, tuple):
        return list(value), None

    if isinstance(value, str):
        if is_invalid_serialized_value(value):
            return None, (
                f"{param_name} received invalid value: '{value}'. "
                "Expected a JSON array like [\"item1\", \"item2\"]"
            )

        parsed = parse_json_payload(value, _warn_legacy=False)
        if isinstance(parsed, list):
            return parsed, None

        return None, (
            f"{param_name} must be a JSON array (list), "
            f"got string that parsed to {type(parsed).__name__}"
        )

    return None, f"{param_name} must be a list or JSON string, got {type(value).__name__}"


def normalize_object_or_list(
    value: Any,
    param_name: str = "value",
) -> tuple[dict[str, Any] | list[Any] | None, str | None]:
    """
    Normalize a parameter that must be either an object or an array.

    Returns:
        Tuple of (parsed_value, error_message). If error_message is set, parsed_value is None.
    """
    if value is None:
        return None, None

    if isinstance(value, dict):
        return value, None

    if isinstance(value, list):
        return value, None

    if isinstance(value, tuple):
        return list(value), None

    if isinstance(value, str):
        if is_invalid_serialized_value(value):
            return None, (
                f"{param_name} received invalid value: '{value}'. "
                "Expected a JSON object or JSON array"
            )
        parsed = parse_json_payload(value, _warn_legacy=False)
        if isinstance(parsed, (dict, list)):
            return parsed, None

        return None, (
            f"{param_name} must be a JSON object or JSON array, "
            f"got string that parsed to {type(parsed).__name__}"
        )

    return None, f"{param_name} must be a dict/list or JSON string, got {type(value).__name__}"


def normalize_json_value(
    value: Any,
    param_name: str = "value",
    *,
    reject_invalid_placeholder: bool = True,
) -> tuple[Any, str | None]:
    """
    Parse string input as JSON when possible, leaving non-string values unchanged.

    Returns:
        Tuple of (parsed_value, error_message).
    """
    if value is None:
        return None, None

    if isinstance(value, str):
        if reject_invalid_placeholder and is_invalid_object_string_placeholder(value):
            return None, f"{param_name} received invalid input: '{value}'"
        return parse_json_payload(value, _warn_legacy=False), None

    return value, None


def normalize_non_placeholder_string(
    value: Any,
    param_name: str = "value",
    *,
    allow_non_string: bool = True,
) -> tuple[Any, str | None]:
    """Reject placeholder strings while preserving valid strings or non-string values."""
    if value is None:
        return None, None

    if isinstance(value, str):
        if is_invalid_object_string_placeholder(value):
            return None, f"{param_name} received invalid input: '{value}'"
        return value, None

    if allow_non_string:
        return value, None

    return None, f"{param_name} must be a string, got {type(value).__name__}"


def normalize_vector3(value: Any, param_name: str = "vector") -> tuple[list[float] | None, str | None]:
    """
    Normalize a vector parameter to [x, y, z] format.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - list/tuple [x, y, z] -> ([x, y, z], None)
    - dict {x, y, z} -> ([x, y, z], None)
    - JSON string "[x, y, z]" or "{x, y, z}" -> parsed and normalized
    - comma-separated string "x, y, z" -> ([x, y, z], None)

    Returns:
        Tuple of (parsed_vector, error_message). If error_message is set, parsed_vector is None.
    """
    if value is None:
        return None, None

    # Handle dict with x/y/z keys (e.g., {"x": 0, "y": 1, "z": 2})
    if isinstance(value, dict):
        if all(k in value for k in ("x", "y", "z")):
            try:
                vec = [float(value["x"]), float(value["y"]), float(value["z"])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {value}"
            except (ValueError, TypeError, KeyError):
                return None, f"{param_name} dict values must be numbers, got {value}"
        return None, f"{param_name} dict must have 'x', 'y', 'z' keys, got {list(value.keys())}"

    # If already a list/tuple with 3 elements, convert to floats
    if isinstance(value, (list, tuple)) and len(value) == 3:
        try:
            vec = [float(value[0]), float(value[1]), float(value[2])]
            if all(math.isfinite(n) for n in vec):
                return vec, None
            return None, f"{param_name} values must be finite numbers, got {value}"
        except (ValueError, TypeError):
            return None, f"{param_name} values must be numbers, got {value}"

    # Try parsing as string
    if isinstance(value, str):
        # Check for obviously invalid values
        if is_invalid_serialized_value(value):
            return None, f"{param_name} received invalid value: '{value}'. Expected [x, y, z] array or {{x, y, z}} object"

        parsed = parse_json_payload(value, _warn_legacy=False)

        # Handle parsed dict
        if isinstance(parsed, dict):
            return normalize_vector3(parsed, param_name)

        # Handle parsed list
        if isinstance(parsed, list) and len(parsed) == 3:
            try:
                vec = [float(parsed[0]), float(parsed[1]), float(parsed[2])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {parsed}"
            except (ValueError, TypeError):
                return None, f"{param_name} values must be numbers, got {parsed}"

        # Handle comma-separated strings "1,2,3", "[1,2,3]", or "(1,2,3)"
        s = value.strip()
        if (s.startswith("[") and s.endswith("]")) or (s.startswith("(") and s.endswith(")")):
            s = s[1:-1]
        parts = [p.strip() for p in (s.split(",") if "," in s else s.split())]
        if len(parts) == 3:
            try:
                vec = [float(parts[0]), float(parts[1]), float(parts[2])]
                if all(math.isfinite(n) for n in vec):
                    return vec, None
                return None, f"{param_name} values must be finite numbers, got {value}"
            except (ValueError, TypeError):
                return None, f"{param_name} values must be numbers, got {value}"

        return None, f"{param_name} must be a [x, y, z] array or {{x, y, z}} object, got: {value}"

    return None, f"{param_name} must be a list, dict, or string, got {type(value).__name__}"


def normalize_string_list(value: Any, param_name: str = "list") -> tuple[list[str] | None, str | None]:
    """
    Normalize a string list parameter that might be a JSON string or plain string.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - list/tuple of strings -> (list, None)
    - JSON string '["a", "b", "c"]' -> parsed and normalized
    - Plain non-JSON string "foo" -> treated as ["foo"]

    Returns:
        Tuple of (parsed_list, error_message). If error_message is set, parsed_list is None.
    """
    if value is None:
        return None, None

    # Already a list/tuple - validate and return
    if isinstance(value, (list, tuple)):
        # Ensure all elements are strings
        if all(isinstance(item, str) for item in value):
            return list(value), None
        return None, f"{param_name} must contain only strings, got mixed types"

    # Try parsing as JSON string (immediate parsing for string input)
    if isinstance(value, str):
        val_trimmed = value.strip()
        # Check for obviously invalid values
        if is_invalid_serialized_value(val_trimmed):
            return None, f"{param_name} received invalid value: '{value}'. Expected a JSON array like [\"item1\", \"item2\"]"

        # Check if it looks like a JSON array but will fail to parse
        looks_like_json_array = (val_trimmed.startswith("[") and val_trimmed.endswith("]"))

        parsed = parse_json_payload(value, _warn_legacy=False)
        # If parsing succeeded and result is a list, validate and return
        if isinstance(parsed, list):
            # Validate all elements are strings
            if all(isinstance(item, str) for item in parsed):
                return parsed, None
            return None, f"{param_name} must contain only strings, got: {parsed}"
        # If parsing returned the original string but it looked like a JSON array,
        # it's malformed JSON - return error instead of treating as single item
        if parsed == value and looks_like_json_array:
            return None, f"{param_name} has invalid JSON syntax: '{value}'. Expected a valid JSON array like [\"item1\", \"item2\"]"
        # If parsing returned the original string (plain non-JSON), treat as single item
        if parsed == value:
            # Treat as single-element list
            return [value], None

        return None, f"{param_name} must be a JSON array (list), got string that parsed to {type(parsed).__name__}"

    return None, f"{param_name} must be a list or JSON string, got {type(value).__name__}"


def _add_alpha_if_needed(color: list[float], output_range: str) -> list[float]:
    """Add alpha channel if color has only 3 components."""
    if len(color) == 3:
        alpha = 1.0 if output_range == "float" else 255
        color.append(alpha)
    return color


def _convert_color_range(components: list[float] | list[int], output_range: str, from_hex: bool = False) -> list:
    """Convert color components to the requested output range."""
    # Convert int components to float for consistent handling
    if isinstance(components[0], int):
        components = [float(c) for c in components]

    if output_range == "int":
        if from_hex:
            return [int(c) for c in components]
        if all(0 <= c <= 1 for c in components):
            return [int(round(c * 255)) for c in components]
        return [int(c) for c in components]
    else:
        if from_hex:
            return [c / 255.0 for c in components]
        if any(c > 1 for c in components):
            return [c / 255.0 for c in components]
        return [float(c) for c in components]


def _parse_hex_color(hex_str: str, output_range: str) -> tuple[list[float] | None, str | None]:
    """Parse hex color string like #RGB, #RRGGBB, #RRGGBBAA."""
    h = hex_str.lstrip("#")
    try:
        if len(h) == 3:
            components = [int(c + c, 16) for c in h] + [255]
            return _convert_color_range(components, output_range, from_hex=True), None
        elif len(h) == 6:
            components = [int(h[i:i+2], 16) for i in (0, 2, 4)] + [255]
            return _convert_color_range(components, output_range, from_hex=True), None
        elif len(h) == 8:
            components = [int(h[i:i+2], 16) for i in (0, 2, 4, 6)]
            return _convert_color_range(components, output_range, from_hex=True), None
        return None, f"Invalid hex color length: {hex_str}"
    except ValueError:
        return None, f"Invalid hex color: {hex_str}"


def _parse_color_dict(value: dict, output_range: str) -> tuple[list[float] | None, str | None]:
    """Parse color from dict with r, g, b keys."""
    if not all(k in value for k in ("r", "g", "b")):
        return None, f"color dict must have 'r', 'g', 'b' keys, got {list(value.keys())}"

    try:
        color = [float(value["r"]), float(value["g"]), float(value["b"])]
        if "a" in value:
            color.append(float(value["a"]))
        color = _add_alpha_if_needed(color, output_range)
        return _convert_color_range(color, output_range), None
    except (ValueError, TypeError, KeyError):
        return None, f"color dict values must be numbers, got {value}"


def _parse_color_list(value: list | tuple, output_range: str) -> tuple[list[float] | None, str | None]:
    """Parse color from list/tuple with 3 or 4 components."""
    if len(value) not in (3, 4):
        return None, f"color must have 3 or 4 components, got {len(value)}"

    try:
        color = [float(c) for c in value]
        color = _add_alpha_if_needed(color, output_range)
        return _convert_color_range(color, output_range), None
    except (ValueError, TypeError):
        return None, f"color values must be numbers, got {value}"


def _parse_color_string(value: str, output_range: str) -> tuple[list[float] | None, str | None]:
    """Parse color from string (hex, JSON, or tuple-style)."""
    if is_invalid_serialized_value(value):
        return None, f"color received invalid value: '{value}'. Expected [r, g, b, a] or {{r, g, b, a}}"

    # Handle hex colors
    if value.startswith("#"):
        return _parse_hex_color(value, output_range)

    # Try parsing as JSON
    parsed = parse_json_payload(value, _warn_legacy=False)

    if isinstance(parsed, dict):
        return _parse_color_dict(parsed, output_range)

    if isinstance(parsed, (list, tuple)) and len(parsed) in (3, 4):
        return _parse_color_list(parsed, output_range)

    # Handle tuple-style strings "(r, g, b)" or "(r, g, b, a)"
    s = value.strip()
    if (s.startswith("[") and s.endswith("]")) or (s.startswith("(") and s.endswith(")")):
        s = s[1:-1]
    parts = [p.strip() for p in s.split(",")]
    if len(parts) in (3, 4):
        try:
            color = [float(p) for p in parts]
            color = _add_alpha_if_needed(color, output_range)
            return _convert_color_range(color, output_range), None
        except (ValueError, TypeError):
            pass

    return None, f"Failed to parse color string: {value}"


def normalize_color(value: Any, output_range: str = "float") -> tuple[list[float] | None, str | None]:
    """
    Normalize a color parameter to [r, g, b, a] format.

    Handles various input formats from MCP clients/LLMs:
    - None -> (None, None)
    - list/tuple [r, g, b] or [r, g, b, a] -> normalized with optional alpha
    - dict {r, g, b} or {r, g, b, a} -> converted to list
    - hex string "#RGB", "#RRGGBB", "#RRGGBBAA" -> parsed to [r, g, b, a]
    - JSON string -> parsed and normalized

    Args:
        value: The color value to normalize
        output_range: "float" for 0.0-1.0 range, "int" for 0-255 range

    Returns:
        Tuple of (parsed_color, error_message). If error_message is set, parsed_color is None.
    """
    if value is None:
        return None, None

    if isinstance(value, dict):
        return _parse_color_dict(value, output_range)

    if isinstance(value, (list, tuple)):
        return _parse_color_list(value, output_range)

    if isinstance(value, str):
        return _parse_color_string(value, output_range)

    return None, f"color must be a list, dict, hex string, or JSON string, got {type(value).__name__}"
