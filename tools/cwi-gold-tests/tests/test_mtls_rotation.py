import os
import subprocess
import time

import pytest
import requests


pytestmark = pytest.mark.simulation


def test_mtls_rotation_returns_green(base_url):
    rotate_cmd = os.environ.get("CWI_MTLS_ROTATION_CMD")
    if not rotate_cmd:
        pytest.skip("CWI_MTLS_ROTATION_CMD not set")

    health_url = os.environ.get("CWI_MTLS_HEALTH_URL", f"{base_url}/api/ready")
    timeout_seconds = int(os.environ.get("CWI_MTLS_ROTATION_TIMEOUT_SECONDS", "120"))
    poll_interval = float(os.environ.get("CWI_MTLS_ROTATION_POLL_SECONDS", "5"))

    result = subprocess.run(rotate_cmd, shell=True)
    assert result.returncode == 0

    deadline = time.time() + timeout_seconds
    last_status = None
    while time.time() < deadline:
        try:
            resp = requests.get(health_url, timeout=10)
            last_status = resp.status_code
            if resp.status_code == 200:
                if resp.headers.get("Content-Type", "").startswith("application/json") and resp.content:
                    payload = resp.json()
                    if payload.get("ready") is False:
                        time.sleep(poll_interval)
                        continue
                return
        except requests.exceptions.RequestException:
            pass
        time.sleep(poll_interval)

    pytest.fail(f"mTLS rotation did not return green within timeout. last_status={last_status}")
