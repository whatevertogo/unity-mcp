from __future__ import annotations

import asyncio
import inspect
import os
import sys
import time
from collections.abc import Awaitable, Callable
from functools import wraps
from typing import Any, cast

from models import MCPResponse


def _in_pytest() -> bool:
    # Integration tests in this repo stub transports and do not run against a live Unity editor.
    # Preflight must be a no-op in that environment to avoid breaking the existing test suite.
    return bool(os.environ.get("PYTEST_CURRENT_TEST"))


def _busy(reason: str, retry_after_ms: int) -> MCPResponse:
    return MCPResponse(
        success=False,
        error="busy",
        message=reason,
        hint="retry",
        data={"reason": reason, "retry_after_ms": int(retry_after_ms)},
    )


async def preflight(
    ctx,
    *,
    requires_no_tests: bool = False,
    wait_for_no_compile: bool = False,
    refresh_if_dirty: bool = False,
    max_wait_s: float = 30.0,
) -> MCPResponse | None:
    """
    Server-side preflight guard used by tools so they behave safely even if the client never reads resources.

    Returns:
      - MCPResponse busy/retry payload when the tool should not proceed right now
      - None when the tool should proceed normally
    """
    if _in_pytest():
        return None

    # Load canonical editor state (server enriches advice + staleness).
    try:
        from services.resources.editor_state import get_editor_state
        state_resp = await get_editor_state(ctx)
        state = state_resp.model_dump() if hasattr(
            state_resp, "model_dump") else state_resp
    except Exception:
        # If we cannot determine readiness, fall back to proceeding (tools already contain retry logic).
        return None

    if not isinstance(state, dict) or not state.get("success", False):
        # Unknown state; proceed rather than blocking (avoids false positives when Unity is reachable but status isn't).
        return None

    data = state.get("data")
    if not isinstance(data, dict):
        return None

    # Optional refresh-if-dirty
    if refresh_if_dirty:
        assets = data.get("assets")
        if isinstance(assets, dict) and assets.get("external_changes_dirty") is True:
            try:
                from services.tools.refresh_unity import refresh_unity
                await refresh_unity(ctx, mode="if_dirty", scope="all", compile="request", wait_for_ready=True)
            except Exception:
                # Best-effort only; fall through to normal tool dispatch.
                pass

    # Tests running: fail fast for tools that require exclusivity.
    if requires_no_tests:
        tests = data.get("tests")
        if isinstance(tests, dict) and tests.get("is_running") is True:
            return _busy("tests_running", 5000)

    # Compilation: optionally wait for a bounded time.
    if wait_for_no_compile:
        deadline = time.monotonic() + float(max_wait_s)
        while True:
            compilation = data.get("compilation") if isinstance(
                data, dict) else None
            is_compiling = isinstance(compilation, dict) and compilation.get(
                "is_compiling") is True
            is_domain_reload_pending = isinstance(compilation, dict) and compilation.get(
                "is_domain_reload_pending") is True
            if not is_compiling and not is_domain_reload_pending:
                break
            if time.monotonic() >= deadline:
                return _busy("compiling", 500)
            await asyncio.sleep(0.25)

            # Refresh state for the next loop iteration.
            try:
                from services.resources.editor_state import get_editor_state
                state_resp = await get_editor_state(ctx)
                state = state_resp.model_dump() if hasattr(
                    state_resp, "model_dump") else state_resp
                data = state.get("data") if isinstance(state, dict) else None
                if not isinstance(data, dict):
                    return None
            except Exception:
                return None

    # Staleness: if the snapshot is stale, proceed (tools will still run), but callers that read resources can back off.
    # In future we may make this strict for some tools.
    return None


def preflight_guard(
    *,
    requires_no_tests: bool = False,
    wait_for_no_compile: bool = False,
    refresh_if_dirty: bool = False,
    max_wait_s: float = 30.0,
    action_arg: str = "action",
    skip_actions: set[str] | None = None,
) -> Callable[[Callable[..., Awaitable[Any]]], Callable[..., Awaitable[Any]]]:
    """
    Declarative preflight decorator for tool handlers.

    Example:
        @preflight_guard(wait_for_no_compile=True, refresh_if_dirty=True)
        async def manage_asset(ctx, action, ...): ...

        @preflight_guard(wait_for_no_compile=True, refresh_if_dirty=True, skip_actions={"ping"})
        async def manage_material(ctx, action, ...): ...
    """
    normalized_skip_actions = {a.lower() for a in (skip_actions or set())}

    def decorator(func: Callable[..., Awaitable[Any]]) -> Callable[..., Awaitable[Any]]:
        signature = inspect.signature(func)
        parameter_names = list(signature.parameters.keys())
        action_index = parameter_names.index(action_arg) if action_arg in signature.parameters else None

        @wraps(func)
        async def wrapped(*args: Any, **kwargs: Any) -> Any:
            action_value: Any = kwargs.get(action_arg)
            if action_value is None and action_index is not None and len(args) > action_index:
                action_value = args[action_index]

            should_skip = isinstance(action_value, str) and action_value.lower() in normalized_skip_actions
            if not should_skip:
                ctx = args[0] if args else kwargs.get("ctx")
                module = sys.modules.get(func.__module__)
                module_preflight = getattr(module, "preflight", None) if module is not None else None
                preflight_callable: Callable[..., Awaitable[MCPResponse | None]]
                if callable(module_preflight) and inspect.iscoroutinefunction(module_preflight):
                    preflight_callable = cast(Callable[..., Awaitable[MCPResponse | None]], module_preflight)
                else:
                    preflight_callable = preflight
                gate = await preflight_callable(
                    ctx,
                    requires_no_tests=requires_no_tests,
                    wait_for_no_compile=wait_for_no_compile,
                    refresh_if_dirty=refresh_if_dirty,
                    max_wait_s=max_wait_s,
                )
                if gate is not None:
                    return gate.model_dump() if hasattr(gate, "model_dump") else gate

            return await func(*args, **kwargs)

        return wrapped

    return decorator
