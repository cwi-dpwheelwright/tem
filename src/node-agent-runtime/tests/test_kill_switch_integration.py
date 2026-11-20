from __future__ import annotations

import json
import os
from urllib import parse, request

import pytest

from node_agent_runtime.config import KillSwitchConfig
from node_agent_runtime.kill_switch import fetch_kill_switch_state


def test_kill_switch_sync_integration():
    admin_url = os.getenv("CWI_KILL_SWITCH_ADMIN_URL")
    token = os.getenv("CWI_BREAK_GLASS_TOKEN")
    issue_url = os.getenv("CWI_BREAK_GLASS_ISSUE_URL")
    tenant_id = os.getenv("CWI_TENANT_ID")
    if not admin_url or not tenant_id:
        pytest.skip("CWI_KILL_SWITCH_ADMIN_URL and CWI_TENANT_ID required")

    if not token:
        issue_url = issue_url or _derive_issue_url(admin_url)
        role = os.getenv("CWI_BREAK_GLASS_ROLE")
        scope = os.getenv("CWI_BREAK_GLASS_SCOPE")
        user_id = os.getenv("CWI_BREAK_GLASS_USER_ID")
        duration = os.getenv("CWI_BREAK_GLASS_DURATION_MINUTES", "5")
        reason = os.getenv("CWI_BREAK_GLASS_REASON")
        if not issue_url or not role or not scope or not user_id:
            pytest.skip(
                "CWI_BREAK_GLASS_TOKEN or issue env vars "
                "(CWI_BREAK_GLASS_ISSUE_URL, CWI_BREAK_GLASS_ROLE, "
                "CWI_BREAK_GLASS_SCOPE, CWI_BREAK_GLASS_USER_ID) required"
            )
        token = _issue_break_glass_token(
            issue_url=issue_url,
            tenant_id=tenant_id,
            user_id=user_id,
            role=role,
            scope=scope,
            duration_minutes=_parse_duration(duration),
            reason=reason,
        )
        os.environ["CWI_BREAK_GLASS_TOKEN"] = token

    config = KillSwitchConfig(
        admin_url=admin_url,
        tenant_id=tenant_id,
        break_glass_token_env="CWI_BREAK_GLASS_TOKEN",
    )
    state = fetch_kill_switch_state(config)
    assert isinstance(state.enabled, bool)
    assert isinstance(state.irreversible, bool)


def _issue_break_glass_token(
    *,
    issue_url: str,
    tenant_id: str,
    user_id: str,
    role: str,
    scope: str,
    duration_minutes: int,
    reason: str | None,
) -> str:
    payload = {
        "tenant_id": tenant_id,
        "user_id": user_id,
        "role": role,
        "scope": scope,
        "duration_minutes": duration_minutes,
    }
    if reason:
        payload["reason"] = reason
    data = json.dumps(payload).encode("utf-8")
    req = request.Request(
        issue_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=10) as resp:
        body = resp.read().decode("utf-8")
    response = json.loads(body)
    token = response.get("token_id")
    if not isinstance(token, str) or not token.strip():
        raise AssertionError("Break-glass issue response missing token_id")
    return token.strip()


def _derive_issue_url(admin_url: str) -> str | None:
    parsed = parse.urlparse(admin_url)
    if not parsed.scheme or not parsed.netloc:
        return None
    return parse.urlunparse((parsed.scheme, parsed.netloc, "/api/break-glass/issue", "", "", ""))


def _parse_duration(value: str) -> int:
    try:
        parsed = int(value.strip())
    except (TypeError, ValueError) as exc:
        raise AssertionError("CWI_BREAK_GLASS_DURATION_MINUTES must be an integer") from exc
    return max(parsed, 1)
