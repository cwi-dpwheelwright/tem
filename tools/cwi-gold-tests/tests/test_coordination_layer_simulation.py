import os
import pytest
import requests


pytestmark = pytest.mark.simulation


def _coordination_url():
    url = os.environ.get("CWI_COORDINATION_URL")
    if not url:
        pytest.skip("CWI_COORDINATION_URL not set")
    return url.rstrip("/")


def _base_url():
    url = os.environ.get("CWI_BASE_URL")
    if not url:
        pytest.skip("CWI_BASE_URL not set")
    return url.rstrip("/")


def _admin_headers():
    token = os.environ.get("CWI_COORDINATION_ADMIN_TOKEN", "").strip()
    if not token:
        return {}
    header = os.environ.get("CWI_COORDINATION_ADMIN_HEADER", "X-Admin-Token")
    return {header: token}


def _require_admin():
    required = os.environ.get("CWI_COORDINATION_REQUIRE_ADMIN", "").lower()
    return required in ("1", "true", "yes")


def test_coordination_degraded_health_reports_down_node():
    base_url = _coordination_url()

    payloads = [
        {
            "node_id": "node-1",
            "interface": "wifi",
            "signal_quality": 0.9,
            "latency_ms": 80,
            "packet_loss": 0.02,
            "bandwidth_kbps": 1200,
        },
        {
            "node_id": "node-2",
            "interface": "cellular",
            "signal_quality": 0.01,
            "latency_ms": 2400,
            "packet_loss": 0.95,
            "bandwidth_kbps": 0,
        },
    ]

    for payload in payloads:
        resp = requests.post(f"{base_url}/api/mesh/links", json=payload, timeout=10)
        assert resp.status_code == 200

    health = requests.get(f"{base_url}/api/mesh/health", timeout=10)
    assert health.status_code == 200

    data = health.json()
    assert data.get("status") in ("down", "degraded")
    assert data.get("down_nodes", 0) >= 1
    assert data.get("down_links", 0) >= 1


def test_coordination_reroute_guidance_emits_event():
    base_url = _coordination_url()
    headers = _admin_headers()
    if _require_admin() and not headers:
        pytest.skip("Coordination admin token required but not provided")

    payloads = [
        {
            "node_id": "node-1",
            "interface": "wifi",
            "signal_quality": 0.2,
            "latency_ms": 800,
            "packet_loss": 0.25,
            "bandwidth_kbps": 400,
        },
        {
            "node_id": "node-1",
            "interface": "ethernet",
            "signal_quality": 0.9,
            "latency_ms": 50,
            "packet_loss": 0.01,
            "bandwidth_kbps": 12000,
        },
    ]

    for payload in payloads:
        resp = requests.post(f"{base_url}/api/mesh/links", json=payload, timeout=10)
        assert resp.status_code == 200

    reroute_payload = {
        "node_id": "node-1",
        "current_interface": "wifi",
        "task_complexity": "high",
    }

    reroute = requests.post(
        f"{base_url}/api/mesh/reroute",
        json=reroute_payload,
        headers=headers,
        timeout=10,
    )
    assert reroute.status_code == 200
    data = reroute.json()
    assert data.get("reroute_required") is True
    assert data.get("preferred_interface") == "ethernet"

    events = requests.get(f"{base_url}/api/mesh/events?limit=10", timeout=10)
    assert events.status_code == 200
    event_types = [evt.get("event_type") for evt in events.json()]
    assert "reroutesuggested" in event_types or "reroute_suggested" in event_types


def test_coordination_routing_hint_includes_stability_fields():
    base_url = _coordination_url()

    payloads = [
        {
            "node_id": "node-9",
            "interface": "wifi",
            "signal_quality": 0.15,
            "latency_ms": 900,
            "packet_loss": 0.4,
            "bandwidth_kbps": 200,
        },
        {
            "node_id": "node-9",
            "interface": "ethernet",
            "signal_quality": 0.95,
            "latency_ms": 30,
            "packet_loss": 0.01,
            "bandwidth_kbps": 15000,
        },
    ]

    for payload in payloads:
        resp = requests.post(f"{base_url}/api/mesh/links", json=payload, timeout=10)
        assert resp.status_code == 200

    hint_payload = {"node_id": "node-9", "task_complexity": "high"}
    hint = requests.post(f"{base_url}/api/mesh/hints", json=hint_payload, timeout=10)
    assert hint.status_code == 200
    data = hint.json()
    assert data.get("node_id") == "node-9"
    assert data.get("preferred_interface") == "ethernet"
    assert data.get("cooldown_seconds", 0) > 0
    assert data.get("hysteresis_margin", -1) >= 0


def test_reroute_event_visible_via_gateway():
    base_url = _base_url()
    coordination_url = _coordination_url()
    if base_url == coordination_url:
        pytest.skip("Gateway base URL not configured for coordination routing")
    headers = _admin_headers()
    if _require_admin() and not headers:
        pytest.skip("Coordination admin token required but not provided")

    payloads = [
        {
            "node_id": "node-7",
            "interface": "wifi",
            "signal_quality": 0.2,
            "latency_ms": 850,
            "packet_loss": 0.3,
            "bandwidth_kbps": 300,
        },
        {
            "node_id": "node-7",
            "interface": "ethernet",
            "signal_quality": 0.9,
            "latency_ms": 40,
            "packet_loss": 0.02,
            "bandwidth_kbps": 14000,
        },
    ]

    for payload in payloads:
        resp = requests.post(f"{coordination_url}/api/mesh/links", json=payload, timeout=10)
        assert resp.status_code == 200

    reroute_payload = {
        "node_id": "node-7",
        "current_interface": "wifi",
        "task_complexity": "high",
    }

    reroute = requests.post(
        f"{coordination_url}/api/mesh/reroute",
        json=reroute_payload,
        headers=headers,
        timeout=10,
    )
    assert reroute.status_code == 200

    events = requests.get(f"{base_url}/coordination/api/mesh/events?limit=10", timeout=10)
    assert events.status_code == 200
    event_types = [evt.get("event_type") for evt in events.json()]
    assert "reroutesuggested" in event_types or "reroute_suggested" in event_types


def test_router_consumes_reroute_guidance_and_updates_hint():
    coordination_url = _coordination_url()
    router_url = os.environ.get("CWI_ROUTER_BASE_URL") or os.environ.get("ROUTER_BASE_URL")
    if not router_url:
        pytest.skip("CWI_ROUTER_BASE_URL not set")
    router_url = router_url.rstrip("/")

    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")
    headers = {"Authorization": f"Bearer {token}"}

    payloads = [
        {
            "node_id": "node-11",
            "interface": "wifi",
            "signal_quality": 0.12,
            "latency_ms": 900,
            "packet_loss": 0.35,
            "bandwidth_kbps": 180,
        },
        {
            "node_id": "node-11",
            "interface": "ethernet",
            "signal_quality": 0.92,
            "latency_ms": 35,
            "packet_loss": 0.01,
            "bandwidth_kbps": 16000,
        },
    ]

    for payload in payloads:
        resp = requests.post(f"{coordination_url}/api/mesh/links", json=payload, timeout=10)
        assert resp.status_code == 200

    hint_payload = {"node_id": "node-11", "task_complexity": "high"}
    hint = requests.post(f"{router_url}/api/mesh/hints", json=hint_payload, headers=headers, timeout=10)
    assert hint.status_code == 200
    data = hint.json()
    assert data.get("node_id") == "node-11"
    assert data.get("preferred_interface") == "ethernet"
