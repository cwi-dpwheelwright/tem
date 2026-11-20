import os
import subprocess

import pytest
import requests


pytestmark = pytest.mark.simulation


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


def test_teams_bot_available_when_router_down():
    base_url = os.environ.get("CWI_TEAMS_BOT_BASE_URL", "").strip()
    if not base_url:
        pytest.skip("CWI_TEAMS_BOT_BASE_URL not set")

    if os.environ.get("CWI_EXPECT_ROUTER_DOWN", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_ROUTER_DOWN not set")

    if _compose_cmd() is None:
        pytest.skip("CWI_COMPOSE_FILE or CWI_COMPOSE_ENV_FILE not set for router-down simulation")

    _compose_run("stop", "cwi-router")
    try:
        health = requests.get(f"{base_url}/health", timeout=10)
        assert health.status_code == 200

        event_payload = {
            "ticket_id": "sim-ticket-1",
            "status": "pending",
            "risk": "medium",
            "reason": "router down simulation",
        }
        response = requests.post(f"{base_url}/api/approvals/events", json=event_payload, timeout=10)
        assert response.status_code == 200
    finally:
        _compose_run("start", "cwi-router")


def test_teams_bot_available_when_orchestrator_down():
    base_url = os.environ.get("CWI_TEAMS_BOT_BASE_URL", "").strip()
    if not base_url:
        pytest.skip("CWI_TEAMS_BOT_BASE_URL not set")

    if os.environ.get("CWI_EXPECT_ORCHESTRATOR_DOWN", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_ORCHESTRATOR_DOWN not set")

    if _compose_cmd() is None:
        pytest.skip("CWI_COMPOSE_FILE or CWI_COMPOSE_ENV_FILE not set for orchestrator-down simulation")

    _compose_run("stop", "cwi-orchestrator")
    try:
        health = requests.get(f"{base_url}/health", timeout=10)
        assert health.status_code == 200

        event_payload = {
            "ticket_id": "sim-ticket-2",
            "status": "pending",
            "risk": "low",
            "reason": "orchestrator down simulation",
        }
        response = requests.post(f"{base_url}/api/approvals/events", json=event_payload, timeout=10)
        assert response.status_code == 200
    finally:
        _compose_run("start", "cwi-orchestrator")
