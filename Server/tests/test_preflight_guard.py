import pytest

from models import MCPResponse
from services.tools import preflight as preflight_module


@pytest.mark.asyncio
async def test_preflight_guard_blocks_and_returns_gate_payload(monkeypatch):
    async def fake_preflight(_ctx, **_kwargs):
        return MCPResponse(success=False, error="busy", message="compiling")

    monkeypatch.setattr(preflight_module, "preflight", fake_preflight)

    @preflight_module.preflight_guard(wait_for_no_compile=True)
    async def _tool(ctx, action):
        return {"success": True, "message": "should_not_run"}

    result = await _tool(object(), "create")
    assert isinstance(result, dict)
    assert result.get("success") is False
    assert result.get("error") == "busy"


@pytest.mark.asyncio
async def test_preflight_guard_skips_configured_actions(monkeypatch):
    call_count = {"count": 0}

    async def fake_preflight(_ctx, **_kwargs):
        call_count["count"] += 1
        return None

    monkeypatch.setattr(preflight_module, "preflight", fake_preflight)

    @preflight_module.preflight_guard(skip_actions={"ping"})
    async def _tool(ctx, action):
        return {"success": True, "action": action}

    result = await _tool(object(), "ping")
    assert result["success"] is True
    assert call_count["count"] == 0


@pytest.mark.asyncio
async def test_preflight_guard_runs_on_non_skipped_actions(monkeypatch):
    call_count = {"count": 0}

    async def fake_preflight(_ctx, **_kwargs):
        call_count["count"] += 1
        return None

    monkeypatch.setattr(preflight_module, "preflight", fake_preflight)

    @preflight_module.preflight_guard(skip_actions={"ping"})
    async def _tool(ctx, action):
        return {"success": True, "action": action}

    result = await _tool(object(), "modify")
    assert result["success"] is True
    assert call_count["count"] == 1
