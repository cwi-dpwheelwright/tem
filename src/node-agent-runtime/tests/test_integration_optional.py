from __future__ import annotations

import os
from urllib import request


def _fetch(url: str) -> int:
    req = request.Request(url, method="GET")
    with request.urlopen(req, timeout=3) as resp:
        return resp.status


def test_integration_health_optional():
    base_url = os.getenv("NODE_AGENT_INTEGRATION_URL")
    if not base_url:
        return
    status = _fetch(f"{base_url.rstrip('/')}/health")
    assert status < 500


def test_integration_ready_optional():
    base_url = os.getenv("NODE_AGENT_INTEGRATION_URL")
    if not base_url:
        return
    status = _fetch(f"{base_url.rstrip('/')}/ready")
    assert status in (200, 503)
