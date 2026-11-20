import os
import time
import subprocess

import pytest
import requests


pytestmark = pytest.mark.simulation


def _compose_cmd():
    compose_file = os.environ.get("CWI_COMPOSE_FILE")
    env_file = os.environ.get("CWI_COMPOSE_ENV_FILE")
    if not compose_file or not env_file:
        pytest.skip("CWI_COMPOSE_FILE or CWI_COMPOSE_ENV_FILE not set")
    return ["docker", "compose", "-f", compose_file, "--env-file", env_file]


def _compose_run(*args):
    cmd = _compose_cmd() + list(args)
    subprocess.run(cmd, check=True)


def _wait_for_status(url, expected, timeout_seconds=60):
    deadline = time.time() + timeout_seconds
    last_status = None
    while time.time() < deadline:
        try:
            resp = requests.get(url, timeout=5)
            last_status = resp.status_code
            if isinstance(expected, (list, tuple, set)):
                if last_status in expected:
                    return last_status
            elif last_status == expected:
                return last_status
        except requests.RequestException:
            last_status = None
        time.sleep(2)
    raise AssertionError(f"Timed out waiting for {url} to return {expected}. Last status: {last_status}")


def test_readyz_fails_when_router_down_and_recovers(base_url):
    if os.environ.get("CWI_EXPECT_READINESS_GATES", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_READINESS_GATES not set")

    ready_url = os.environ.get("CWI_READY_URL", f"{base_url}/readyz")

    _wait_for_status(ready_url, 200, timeout_seconds=60)

    _compose_run("stop", "cwi-router")
    try:
        _wait_for_status(ready_url, (502, 503), timeout_seconds=60)
    finally:
        _compose_run("start", "cwi-router")

    _wait_for_status(ready_url, 200, timeout_seconds=60)
