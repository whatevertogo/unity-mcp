import pytest

from .test_helpers import DummyContext

import services.tools.batch_execute as batch_mod
import services.tools.manage_animation as anim_mod
import services.tools.manage_editor as editor_mod
import services.tools.manage_vfx as vfx_mod


@pytest.mark.asyncio
async def test_manage_editor_wait_for_completion_string_coercion(monkeypatch):
    captured = {}

    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(editor_mod, "send_with_unity_instance", fake_send)

    resp = await editor_mod.manage_editor(
        ctx=DummyContext(),
        action="play",
        wait_for_completion="true",
    )

    assert resp["success"] is True
    assert captured["params"]["waitForCompletion"] is True


@pytest.mark.asyncio
async def test_batch_execute_option_string_normalization(monkeypatch):
    captured = {}

    async def fake_limit(_ctx):
        return 100

    async def fake_send(_func, _instance, _tool_name, payload, **_kwargs):
        captured["payload"] = payload
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(batch_mod, "_get_max_commands_from_editor_state", fake_limit)
    monkeypatch.setattr(batch_mod, "send_with_unity_instance", fake_send)

    resp = await batch_mod.batch_execute(
        ctx=DummyContext(),
        commands=[{"tool": "manage_scene", "params": {"action": "get_active"}}],
        parallel="false",
        fail_fast="true",
        max_parallelism="4",
    )

    assert resp["success"] is True
    assert captured["payload"]["parallel"] is False
    assert captured["payload"]["failFast"] is True
    assert captured["payload"]["maxParallelism"] == 4


@pytest.mark.asyncio
async def test_batch_execute_invalid_max_parallelism_raises(monkeypatch):
    async def fake_limit(_ctx):
        return 100

    monkeypatch.setattr(batch_mod, "_get_max_commands_from_editor_state", fake_limit)

    with pytest.raises(ValueError):
        await batch_mod.batch_execute(
            ctx=DummyContext(),
            commands=[{"tool": "manage_scene", "params": {"action": "get_active"}}],
            max_parallelism="not-an-int",
        )


@pytest.mark.asyncio
async def test_manage_animation_properties_json_string(monkeypatch):
    captured = {}

    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(anim_mod, "send_with_unity_instance", fake_send)

    resp = await anim_mod.manage_animation(
        ctx=DummyContext(),
        action="animator_play",
        target="Player",
        properties='{"stateName":"Run","layer":1}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == {"stateName": "Run", "layer": 1}


@pytest.mark.asyncio
async def test_manage_vfx_properties_json_string(monkeypatch):
    captured = {}

    async def fake_send(_func, _instance, _tool_name, params, **_kwargs):
        captured["params"] = params
        return {"success": True, "message": "ok"}

    monkeypatch.setattr(vfx_mod, "send_with_unity_instance", fake_send)

    resp = await vfx_mod.manage_vfx(
        ctx=DummyContext(),
        action="particle_create",
        target="Emitter",
        properties='{"position":[0,1,0]}',
    )

    assert resp["success"] is True
    assert captured["params"]["properties"] == {"position": [0, 1, 0]}
