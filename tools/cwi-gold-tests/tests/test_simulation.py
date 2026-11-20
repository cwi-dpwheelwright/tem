import json
import os
import time
import requests
import pytest
import subprocess



pytestmark = pytest.mark.simulation


def _citation_key(citation):
    return f"{citation.get('doc_id')}:{citation.get('chunk_id')}"


def _compose_cmd():
    compose_file = os.environ.get("CWI_COMPOSE_FILE")
    env_file = os.environ.get("CWI_COMPOSE_ENV_FILE")
    if not compose_file or not env_file:
        return None
    return ["docker", "compose", "-f", compose_file, "--env-file", env_file]


def _compose_run(*args):
    cmd = _compose_cmd()
    if not cmd:
        raise RuntimeError("CWI_COMPOSE_FILE or CWI_COMPOSE_ENV_FILE not set")
    subprocess.run(cmd + list(args), check=True)


def _auth_headers(extra=None):
    headers = dict(extra or {})
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers



def _wait_for_ready(base_url, timeout_seconds=30, poll_seconds=2):
    deadline = time.monotonic() + timeout_seconds
    last_error = None

    while time.monotonic() < deadline:
        try:
            resp = requests.get(f"{base_url}/api/ready", timeout=5)
            if resp.status_code == 200:
                return
            last_error = f"status {resp.status_code}"
        except requests.RequestException as exc:
            last_error = str(exc)

        time.sleep(poll_seconds)

    raise AssertionError(f"Router did not become ready after restart ({last_error}).")


def test_vector_shard1_down_returns_degraded_ask_response(base_url):
    expect_shard_down = os.environ.get("CWI_EXPECT_VECTOR_SHARD1_DOWN", "").lower()
    if expect_shard_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_VECTOR_SHARD1_DOWN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    citations = data.get("citations", [])
    trace = data.get("trace", {})
    sources = trace.get("sources", [])

    assert isinstance(citations, list)
    assert isinstance(sources, list)

    citation_keys = [_citation_key(citation) for citation in citations]
    if citation_keys:
        assert all(key != "None:None" for key in citation_keys)

    assert set(citation_keys) == set(sources)


def test_vector_shard2_down_returns_degraded_ask_response(base_url):
    expect_shard_down = os.environ.get("CWI_EXPECT_VECTOR_SHARD2_DOWN", "").lower()
    if expect_shard_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_VECTOR_SHARD2_DOWN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    citations = data.get("citations", [])
    trace = data.get("trace", {})
    sources = trace.get("sources", [])

    assert isinstance(citations, list)
    assert isinstance(sources, list)

    citation_keys = [_citation_key(citation) for citation in citations]
    if citation_keys:
        assert all(key != "None:None" for key in citation_keys)

    assert set(citation_keys) == set(sources)


def test_both_vector_shards_down_returns_empty_evidence(base_url):
    expect_shards_down = os.environ.get("CWI_EXPECT_VECTOR_SHARDS_DOWN", "").lower()
    if expect_shards_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_VECTOR_SHARDS_DOWN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    citations = data.get("citations", [])
    trace = data.get("trace", {})
    sources = trace.get("sources", [])

    assert isinstance(citations, list)
    assert isinstance(sources, list)
    assert citations == []
    assert sources == []


def test_postgres_down_action_fails_closed(base_url):
    expect_db_down = os.environ.get("CWI_EXPECT_DB_DOWN", "").lower()
    if expect_db_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_DB_DOWN not set")

    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")

    headers = {"Authorization": f"Bearer {token}"}
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 503
    assert resp.json().get("code") == "dependency_unavailable"


def test_redis_down_router_ready_fails(base_url):
    expect_redis_down = os.environ.get("CWI_EXPECT_REDIS_DOWN", "").lower()
    if expect_redis_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_REDIS_DOWN not set")

    resp = requests.get(f"{base_url}/api/ready", timeout=10)
    assert resp.status_code == 503

    data = resp.json()
    assert data.get("ready") is False

    checks = data.get("checks", {})
    redis_check = checks.get("redis", {})
    assert redis_check.get("status") == "down"


def test_ledger_down_orchestrator_ready_and_requests_fail(base_url):
    expect_ledger_down = os.environ.get("CWI_EXPECT_LEDGER_DOWN", "").lower()
    if expect_ledger_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_LEDGER_DOWN not set")

    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")

    ready = requests.get(f"{base_url}/api/ready", timeout=10)
    assert ready.status_code == 503

    ready_data = ready.json()
    assert ready_data.get("ready") is False

    checks = ready_data.get("checks", {})
    provenance = checks.get("provenance", {})
    assert provenance.get("status") == "down"

    headers = {"Authorization": f"Bearer {token}"}
    action_payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    action = requests.post(f"{base_url}/api/action", json=action_payload, headers=headers, timeout=20)
    assert action.status_code == 503
    assert action.json().get("code") == "dependency_unavailable"

    ask_payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": os.environ.get("CWI_PACK_NAME", "enterprise")},
        "context": {"mode": "observe", "top_k": 3},
    }

    ask = requests.post(f"{base_url}/api/ask", json=ask_payload, headers=_auth_headers(), timeout=20)
    assert ask.status_code == 503
    assert ask.json().get("code") == "dependency_unavailable"


def test_pro_model_down_returns_failover_or_governed_error(base_url):
    expect_model_down = os.environ.get("CWI_EXPECT_MODEL_PRO_DOWN", "").lower()
    if expect_model_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_MODEL_PRO_DOWN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    headers = _auth_headers({"x-model-tier": "pro"})
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20)

    if resp.status_code == 200:
        data = resp.json()
        assert "answer" in data
        assert "trace" in data
        return

    assert resp.status_code == 503
    payload = resp.json()
    assert payload.get("error") == "dependency_unavailable" or payload.get("code") == "dependency_unavailable"


def test_gateway_packs_works_when_router_down(base_url):
    expect_router_down = os.environ.get("CWI_EXPECT_ROUTER_DOWN", "").lower()
    if expect_router_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_ROUTER_DOWN not set")

    if _compose_cmd() is None:
        pytest.skip("CWI_COMPOSE_FILE or CWI_COMPOSE_ENV_FILE not set for router-down simulation")

    _compose_run("stop", "cwi-router")
    try:
        pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
        packs = requests.get(f"{base_url}/packs/{pack_name}/pack.json", timeout=10)
        assert packs.status_code == 200

        api = requests.get(f"{base_url}/api/health", timeout=10)
        assert api.status_code == 503
    finally:
        _compose_run("start", "cwi-router")
        _wait_for_ready(base_url)


def test_gateway_packs_fail_closed_when_pack_service_down(base_url):
    expect_packs_down = os.environ.get("CWI_EXPECT_PACK_SERVICE_DOWN", "").lower()
    if expect_packs_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_PACK_SERVICE_DOWN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    packs = requests.get(f"{base_url}/packs/{pack_name}/pack.json", timeout=10)
    assert packs.status_code == 503
    assert "pack_service_unavailable" in packs.text.lower()


def test_corrupted_pack_artifacts_fail_closed(base_url):
    expect_corrupt = os.environ.get("CWI_EXPECT_PACK_CORRUPT", "").lower()
    if expect_corrupt not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_PACK_CORRUPT not set")

    pack_name = os.environ.get("CWI_CORRUPT_PACK_NAME", "corrupt")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code in (400, 422, 503)
    body_text = resp.text.lower()
    if resp.headers.get("Content-Type", "").startswith("application/json") and resp.content:
        payload = resp.json()
        message = str(payload.get("error") or payload.get("message") or payload.get("code") or "").lower()
        body_text = body_text or message
    assert "pack" in body_text


def test_mtls_enforcement_blocks_plaintext(base_url):
    expect_mtls = os.environ.get("CWI_EXPECT_MTLS_ENFORCED", "").lower()
    if expect_mtls not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_MTLS_ENFORCED not set")

    mtls_base_url = os.environ.get("CWI_MTLS_BASE_URL", "").strip()
    if not mtls_base_url:
        pytest.skip("CWI_MTLS_BASE_URL not set")

    try:
        resp = requests.get(f"{mtls_base_url}/api/health", timeout=10, verify=False)
        assert resp.status_code in (400, 401, 403, 495)
    except requests.exceptions.SSLError:
        assert True
    except requests.exceptions.RequestException:
        assert True


def test_survival_mode_signal_triggers_and_recovers(base_url):
    expect_survival = os.environ.get("CWI_EXPECT_SURVIVAL_SIMULATION", "").lower()
    if expect_survival not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_SURVIVAL_SIMULATION not set")

    enter_payload = {
        "signal_type": os.environ.get("CWI_SURVIVAL_SIGNAL_TYPE", "network_compute"),
        "severity": float(os.environ.get("CWI_SURVIVAL_SIGNAL_SEVERITY", "0.95")),
        "details": os.environ.get("CWI_SURVIVAL_SIGNAL_DETAILS", "low network + low compute"),
    }

    enter = requests.post(
        f"{base_url}/api/survival/signal",
        json=enter_payload,
        headers=_auth_headers(),
        timeout=20,
    )
    assert enter.status_code == 200
    enter_data = enter.json()
    expected_enter = os.environ.get("CWI_SURVIVAL_EXPECT_ENTER_MODE", "survival")
    if expected_enter:
        assert enter_data.get("mode") == expected_enter
    assert enter_data.get("offline_message") or expected_enter == "normal"

    recovery_payload = {
        "signal_type": os.environ.get("CWI_SURVIVAL_RECOVERY_SIGNAL_TYPE", "recovery"),
        "severity": float(os.environ.get("CWI_SURVIVAL_RECOVERY_SEVERITY", "0.2")),
        "details": os.environ.get("CWI_SURVIVAL_RECOVERY_DETAILS", "recovery signal"),
    }
    recovery = requests.post(
        f"{base_url}/api/survival/signal",
        json=recovery_payload,
        headers=_auth_headers(),
        timeout=20,
    )
    assert recovery.status_code == 200
    recovery_data = recovery.json()
    expected_recovery = os.environ.get("CWI_SURVIVAL_EXPECT_RECOVERY_MODE", "normal")
    if expected_recovery:
        assert recovery_data.get("mode") == expected_recovery


def test_simulation_timeout_handled_safely(base_url):
    expect_timeout = os.environ.get("CWI_EXPECT_SIMULATION_TIMEOUT", "").lower()
    if expect_timeout not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_SIMULATION_TIMEOUT not set")

    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload_raw = os.environ.get("CWI_SIMULATION_TIMEOUT_ACTION_JSON")
    if not payload_raw:
        pytest.skip("CWI_SIMULATION_TIMEOUT_ACTION_JSON not set")

    payload = json.loads(payload_raw)
    headers = {"Authorization": f"Bearer {token}"}
    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("executed") is False
    assert data.get("approval_ticket") or (data.get("result") or {}).get("status") == "blocked"
