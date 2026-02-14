import pytest

from services.tools.utils import (
    normalize_json_list,
    normalize_object_or_list,
    normalize_param_map,
    normalize_properties,
    rule_bool,
    rule_float,
    rule_int,
    rule_json_list,
    rule_json_value,
    rule_non_placeholder_string,
    rule_object,
    rule_string_list,
    rule_vector3,
    with_normalized_params,
)


@pytest.mark.unit
def test_normalize_param_map_happy_path():
    normalized, error = normalize_param_map(
        {
            "position": "[1, 2, 3]",
            "save_as_prefab": "true",
            "components_to_add": '["BoxCollider", "Rigidbody"]',
            "component_properties": '{"MeshRenderer": {"enabled": true}}',
        },
        [
            rule_vector3("position"),
            rule_bool("save_as_prefab", output_key="saveAsPrefab"),
            rule_string_list("components_to_add", output_key="componentsToAdd"),
            rule_object("component_properties", output_key="componentProperties"),
        ],
    )

    assert error is None
    assert normalized == {
        "position": [1.0, 2.0, 3.0],
        "saveAsPrefab": True,
        "componentsToAdd": ["BoxCollider", "Rigidbody"],
        "componentProperties": {"MeshRenderer": {"enabled": True}},
    }


@pytest.mark.unit
def test_normalize_param_map_returns_error_when_rule_fails():
    normalized, error = normalize_param_map(
        {"position": "1,2"},
        [rule_vector3("position")],
    )

    assert normalized is None
    assert error is not None
    assert "position" in error


@pytest.mark.unit
def test_rule_object_reports_parameter_name_on_invalid_placeholder():
    normalized, error = normalize_param_map(
        {"component_properties": "[object Object]"},
        [rule_object("component_properties", output_key="componentProperties")],
    )

    assert normalized is None
    assert error is not None
    assert "component_properties" in error


@pytest.mark.unit
def test_normalize_properties_keeps_backwards_compatibility():
    parsed, error = normalize_properties('{"foo": 1}')
    assert error is None
    assert parsed == {"foo": 1}


@pytest.mark.unit
def test_rule_int_and_float_coercion_with_defaults():
    normalized, error = normalize_param_map(
        {
            "page_size": "25",
            "noise_scale": "0.125",
        },
        [
            rule_int("page_size", output_key="pageSize", default=50),
            rule_float("noise_scale", output_key="noiseScale", default=0.1),
        ],
    )

    assert error is None
    assert normalized == {"pageSize": 25, "noiseScale": 0.125}


@pytest.mark.unit
def test_rule_json_list_and_object_or_list():
    normalized, error = normalize_param_map(
        {"patches": '[{"op":"set"}]'},
        [rule_json_list("patches")],
    )

    assert error is None
    assert normalized == {"patches": [{"op": "set"}]}

    value, obj_or_list_error = normalize_object_or_list('{"x":1}', "payload")
    assert obj_or_list_error is None
    assert value == {"x": 1}


@pytest.mark.unit
def test_rule_json_value_and_non_placeholder_string():
    normalized, error = normalize_param_map(
        {"value": '{"x": 1}', "label": "ok"},
        [rule_json_value("value"), rule_non_placeholder_string("label")],
    )

    assert error is None
    assert normalized == {"value": {"x": 1}, "label": "ok"}


@pytest.mark.unit
def test_rule_non_placeholder_string_keeps_empty_string_for_compat():
    normalized, error = normalize_param_map(
        {"value": ""},
        [rule_non_placeholder_string("value")],
    )

    assert error is None
    assert normalized == {"value": ""}


@pytest.mark.unit
def test_normalize_json_list_placeholder_error_mentions_param():
    parsed, error = normalize_json_list("[object Object]", "patches")
    assert parsed is None
    assert error is not None
    assert "patches" in error


@pytest.mark.unit
def test_rule_builders_produce_equivalent_rules():
    first = rule_int("page_size", output_key="pageSize")
    second = rule_int("page_size", output_key="pageSize")
    assert first.input_key == second.input_key
    assert first.output_key == second.output_key
    assert first.include_none == second.include_none
    assert first.normalizer("42") == second.normalizer("42")


@pytest.mark.asyncio
async def test_with_normalized_params_decorator_success():
    """Test that decorator normalizes parameters and injects them."""
    @with_normalized_params(
        rule_vector3("position"),
        rule_bool("save_as_prefab", output_key="saveAsPrefab"),
    )
    async def example_func(ctx, action, position=None, save_as_prefab=None, saveAsPrefab=None):
        return {"position": position, "saveAsPrefab": saveAsPrefab}

    result = await example_func(
        ctx=None,
        action="test",
        position="[1, 2, 3]",
        save_as_prefab="true",
    )

    assert result == {"position": [1.0, 2.0, 3.0], "saveAsPrefab": True}


@pytest.mark.asyncio
async def test_with_normalized_params_decorator_error():
    """Test that decorator returns error when normalization fails."""
    @with_normalized_params(
        rule_vector3("position"),
    )
    async def example_func(ctx, action, position=None):
        return {"position": position}

    result = await example_func(
        ctx=None,
        action="test",
        position="invalid",
    )

    assert result["success"] is False
    assert "position" in result["message"]


@pytest.mark.asyncio
async def test_with_normalized_params_custom_error_keys():
    """Test that decorator can customize error response keys."""
    @with_normalized_params(
        rule_vector3("position"),
        error_response_key="ok",
        error_message_key="error",
    )
    async def example_func(ctx, action, position=None):
        return {"position": position}

    result = await example_func(
        ctx=None,
        action="test",
        position="invalid",
    )

    assert result["ok"] is False
    assert "error" in result
    assert "position" in result["error"]
