import os
import subprocess
import sys
import time

import pytest
import requests


pytestmark = pytest.mark.smoke


def _lifecycle_dir() -> str:
    path = os.environ.get("CWI_MODEL_WORKER_LIFECYCLE_DIR", "").strip()
    if not path:
        pytest.skip("CWI_MODEL_WORKER_LIFECYCLE_DIR not set")
    return path


def _script_path(lifecycle_dir: str, name: str) -> str:
    ext = ".ps1" if os.name == "nt" else ".sh"
    path = os.path.join(lifecycle_dir, f"{name}{ext}")
    if not os.path.exists(path):
        pytest.skip(f"{path} not found")
    return path


def _run_script(path: str, *args: str) -> None:
    if os.name == "nt":
        cmd = ["powershell", "-ExecutionPolicy", "Bypass", "-File", path, *args]
    else:
        cmd = ["bash", path, *args]
    subprocess.run(cmd, check=True)


def _wait_for_health(url: str, timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    last_status = None
    while time.time() < deadline:
        try:
            resp = requests.get(url, timeout=5)
            last_status = resp.status_code
            if resp.status_code == 200:
                return
        except requests.RequestException:
            last_status = None
        time.sleep(2)
    raise AssertionError(f"Timed out waiting for {url} to return 200. Last status: {last_status}")


def test_model_worker_lifecycle_ready():
    lifecycle_dir = _lifecycle_dir()
    start_script = os.environ.get("CWI_MODEL_WORKER_START_SCRIPT") or _script_path(lifecycle_dir, "start")
    stop_script = os.environ.get("CWI_MODEL_WORKER_STOP_SCRIPT") or _script_path(lifecycle_dir, "stop")
    health_script = os.environ.get("CWI_MODEL_WORKER_HEALTH_SCRIPT") or _script_path(lifecycle_dir, "health")

    health_url = os.environ.get("CWI_MODEL_WORKER_HEALTH_URL", "").strip()
    if not health_url:
        port = os.environ.get("MODEL_HEALTH_PORT", "").strip()
        if port:
            health_url = f"http://localhost:{port}"
    if not health_url:
        pytest.skip("CWI_MODEL_WORKER_HEALTH_URL or MODEL_HEALTH_PORT not set")

    _run_script(start_script)
    try:
        _wait_for_health(f"{health_url.rstrip('/')}/health")
        _run_script(health_script)
    finally:
        if os.environ.get("CWI_MODEL_WORKER_SKIP_STOP", "").lower() not in ("1", "true", "yes"):
            _run_script(stop_script)
