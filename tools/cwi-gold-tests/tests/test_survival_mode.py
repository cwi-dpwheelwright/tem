import json
import os

import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.patent]


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    headers = {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def _load_payload(env_key):
    value = os.environ.get(env_key)
    if not value:
        return None
    try:
        return json.loads(value)
    except json.JSONDecodeError:
        pass
    if os.path.exists(value):
        with open(value, "r", encoding="utf-8") as handle:
            return json.loads(handle.read())
    return None


def test_survival_mode_blocks_actions(base_url):
    enter_signal = _load_payload("CWI_SURVIVAL_ENTER_SIGNAL_JSON")
    action_payload = _load_payload("CWI_SURVIVAL_ACTION_PAYLOAD_JSON")
    if enter_signal is None or action_payload is None:
        pytest.skip("CWI_SURVIVAL_ENTER_SIGNAL_JSON or CWI_SURVIVAL_ACTION_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/survival/signal", json=enter_signal, timeout=20)
    assert resp.status_code == 200
    status = resp.json()
    assert status.get("mode") in ("survival", "degraded")

    action = requests.post(
        f"{base_url}/api/action",
        json=action_payload,
        headers=_auth_headers(),
        timeout=20,
    )
    assert action.status_code == 200
    data = action.json()
    assert data.get("executed") is False
    assert data.get("approval_ticket") or (data.get("result") or {}).get("status") == "blocked"


def test_survival_recovery_hysteresis(base_url):
    enter_signal = _load_payload("CWI_SURVIVAL_ENTER_SIGNAL_JSON")
    mid_signal = _load_payload("CWI_SURVIVAL_HYSTERESIS_SIGNAL_JSON")
    if enter_signal is None or mid_signal is None:
        pytest.skip("CWI_SURVIVAL_ENTER_SIGNAL_JSON or CWI_SURVIVAL_HYSTERESIS_SIGNAL_JSON not set")

    enter = requests.post(f"{base_url}/api/survival/signal", json=enter_signal, timeout=20)
    assert enter.status_code == 200
    enter_payload = enter.json()
    assert enter_payload.get("mode") in ("survival", "degraded")

    mid = requests.post(f"{base_url}/api/survival/signal", json=mid_signal, timeout=20)
    assert mid.status_code == 200
    mid_payload = mid.json()

    expected_mode = os.environ.get("CWI_SURVIVAL_HYSTERESIS_EXPECT_MODE", "survival")
    if expected_mode:
        assert mid_payload.get("mode") == expected_mode

    recovery_signal = _load_payload("CWI_SURVIVAL_RECOVERY_SIGNAL_JSON")
    if recovery_signal is None:
        return

    recovered = requests.post(f"{base_url}/api/survival/signal", json=recovery_signal, timeout=20)
    assert recovered.status_code == 200
    recovered_payload = recovered.json()
    expected_recovery = os.environ.get("CWI_SURVIVAL_RECOVERY_EXPECT_MODE", "normal")
    if expected_recovery:
        assert recovered_payload.get("mode") == expected_recovery
