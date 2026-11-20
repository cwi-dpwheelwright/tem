import json
import os
import urllib.parse
import urllib.request
from pathlib import Path

import pytest

from edge_agent.metrics import LinkMetricsInterface, LinkMetricsQuality
from edge_agent.metrics_exporter import MetricsExporter, MetricsExporterConfig


pytestmark = pytest.mark.integration


def test_metrics_exporter_coordination_layer_integration(tmp_path) -> None:
    base_url, env = _resolve_coordination_base_url()
    if not base_url:
        pytest.skip("CWI_COORDINATION_BASE_URL or COORDINATION_HOST_PORT/COORDINATION_HOSTPORT required")

    auth_required = _resolve_bool(os.getenv("AUTH_REQUIRED"), env.get("AUTH_REQUIRED"))
    token = os.getenv("CWI_AUTH_TOKEN", "") or env.get("CWI_AUTH_TOKEN", "")
    if auth_required and not token:
        token = _try_issue_token(env)
    if auth_required and not token:
        pytest.skip("AUTH_REQUIRED enabled but unable to obtain auth token")

    config = MetricsExporterConfig(
        endpoints=(base_url,),
        node_id="edge-node-1",
        queue_path=str(tmp_path / "metrics-buffer.jsonl"),
        queue_max_items=10,
        queue_max_bytes=10240,
        queue_drop_policy="drop_oldest",
        timeout_seconds=5.0,
        max_batch=5,
        backoff_base_seconds=0.5,
        backoff_max_seconds=2.0,
        failure_threshold=2,
        recovery_seconds=1.0,
    )
    exporter = MetricsExporter(config, token_provider=lambda: token or "test-token")

    observed = "2026-02-02T00:00:00Z"
    interfaces = [
        LinkMetricsInterface(
            interface_id="eth0",
            interface_type="ethernet",
            state="up",
            observed_utc=observed,
            rx_mbps=5.0,
            tx_mbps=2.0,
            mtu=1500,
            quality=LinkMetricsQuality(
                latency_ms=12.0,
                jitter_ms=1.0,
                loss_ratio=0.01,
                throughput_mbps=40.0,
                rssi_dbm=-50.0,
                snr_db=22.0,
            ),
        )
    ]
    assert exporter.enqueue(interfaces, observed)
    exporter.flush()
    assert exporter.buffer.pending(limit=1) == []


def _resolve_coordination_base_url() -> tuple[str, dict[str, str]]:
    base_url = os.getenv("CWI_COORDINATION_BASE_URL", "").strip()
    if base_url:
        return base_url.rstrip("/"), {}

    env = _load_compose_env()

    hostport = os.getenv("COORDINATION_HOST_PORT", "").strip() or env.get("COORDINATION_HOST_PORT", "")
    if hostport:
        if hostport.isdigit():
            return f"http://localhost:{hostport}", env
        return f"http://{hostport}", env

    hostport = os.getenv("COORDINATION_HOSTPORT", "").strip() or env.get("COORDINATION_HOSTPORT", "")
    if hostport:
        if hostport.isdigit():
            return f"http://localhost:{hostport}", env
        return f"http://{hostport}", env

    return "", env


def _load_compose_env() -> dict[str, str]:
    repo_root = _find_repo_root(Path(__file__).resolve())
    if not repo_root:
        return {}
    env_path = repo_root / "OPS" / "cwi-ops" / "compose" / "test" / ".env"
    if not env_path.exists():
        return {}
    env: dict[str, str] = {}
    for line in env_path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        key, value = stripped.split("=", 1)
        env[key.strip()] = value.strip()
    return env


def _find_repo_root(path: Path) -> Path | None:
    for parent in [path] + list(path.parents):
        if (parent / "OPS").exists() and (parent / "iterations").exists():
            return parent
    return None


def _resolve_bool(primary: str | None, fallback: str | None) -> bool:
    value = primary if primary is not None and primary != "" else fallback
    if value is None:
        return False
    return value.strip().lower() in {"1", "true", "yes", "y"}


def _try_issue_token(env: dict[str, str]) -> str:
    login_url = _resolve_identity_login_url(env)
    tenant_id = env.get("IDENTITY_BOOTSTRAP_TENANT_ID", "")
    username = env.get("IDENTITY_BOOTSTRAP_ADMIN_USERNAME", "")
    password = env.get("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD", "")
    if not login_url or not tenant_id or not username or not password:
        return ""
    payload = {
        "tenant_id": tenant_id,
        "username": username,
        "password": password,
    }
    try:
        data = json.dumps(payload).encode("utf-8")
        request = urllib.request.Request(
            login_url,
            data=data,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(request, timeout=10) as response:
            body = response.read().decode("utf-8")
    except Exception:
        return ""
    try:
        result = json.loads(body)
    except json.JSONDecodeError:
        return ""
    token = result.get("access_token")
    if isinstance(token, str) and token.strip():
        return token.strip()
    return ""


def _resolve_identity_login_url(env: dict[str, str]) -> str:
    base = env.get("IDENTITY_URL", "").strip()
    if base:
        parsed = urllib.parse.urlparse(base)
        if parsed.scheme and parsed.netloc:
            host = parsed.hostname or ""
            if host in {"localhost", "127.0.0.1"}:
                return f"{base.rstrip('/')}/auth/login"
        identity_port = env.get("IDENTITY_HTTP_PORT", "").strip()
        if identity_port:
            return f"http://localhost:{identity_port}/auth/login"
    hostport = env.get("IDENTITY_HOSTPORT", "").strip()
    if hostport:
        host, sep, port = hostport.partition(":")
        if port:
            return f"http://localhost:{port}/auth/login"
        return f"http://localhost:{hostport}/auth/login"
    gateway_port = env.get("GATEWAY_HTTP_PORT", "").strip()
    if gateway_port:
        return f"http://localhost:{gateway_port}/auth/login"
    return ""
