"""Health and readiness evaluation for the node agent runtime."""

from __future__ import annotations

import json
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
import os

from .config import AgentConfig, IdentityConfig, ReadinessConfig
from .degradation import evaluate_degradation, sample_metrics
from urllib import request


@dataclass(frozen=True)
class HealthStatus:
    healthy: bool
    ready: bool
    details: dict[str, Any]


def evaluate_health(config: AgentConfig) -> HealthStatus:
    issues = []
    identity_issues = _identity_issues(config.identity)
    issues.extend(identity_issues)

    state_dir_issue = _state_dir_issue(config.storage.state_dir)
    if state_dir_issue:
        issues.append(state_dir_issue)

    dependency_issues = _dependency_issues(config.readiness)
    issues.extend(dependency_issues)

    metrics = sample_metrics(config.storage.state_dir)
    snapshot = evaluate_degradation(metrics, config.degradation)

    ready = not issues and snapshot.severity < config.readiness.max_degradation_severity
    details = {
        "issues": issues,
        "degradation": snapshot.details,
    }
    return HealthStatus(healthy=True, ready=ready, details=details)


def health_payload(config: AgentConfig) -> dict[str, Any]:
    status = evaluate_health(config)
    return {
        "healthy": status.healthy,
        "ready": status.ready,
        "details": status.details,
    }


def serve_health(config: AgentConfig, host: str, port: int) -> None:
    handler = _make_handler(config)
    server = ThreadingHTTPServer((host, port), handler)
    server.serve_forever()


def _identity_issues(identity: IdentityConfig) -> list[str]:
    issues = []
    if identity.private_key_pem_env and not _env_present(identity.private_key_pem_env):
        issues.append(f"missing env: {identity.private_key_pem_env}")
    if identity.public_key_pem_env and not _env_present(identity.public_key_pem_env):
        issues.append(f"missing env: {identity.public_key_pem_env}")

    if identity.persist_keys:
        if identity.private_key_path and not identity.private_key_path.exists():
            issues.append(f"missing private key: {identity.private_key_path}")
        if identity.public_key_path and not identity.public_key_path.exists():
            issues.append(f"missing public key: {identity.public_key_path}")
    return issues


def _state_dir_issue(state_dir: Path | None) -> str | None:
    if state_dir is None:
        return None
    try:
        state_dir.mkdir(parents=True, exist_ok=True)
    except OSError as exc:
        return f"state_dir not writable: {exc}"
    return None


def _env_present(name: str) -> bool:
    return bool(name and name in os.environ)


def _dependency_issues(readiness: ReadinessConfig) -> list[str]:
    issues = []
    for url in readiness.dependency_urls:
        if not _check_dependency(url, readiness.dependency_timeout_seconds):
            issues.append(f"dependency unavailable: {url}")
    return issues


def _check_dependency(url: str, timeout_seconds: int) -> bool:
    try:
        req = request.Request(url, method="GET")
        with request.urlopen(req, timeout=timeout_seconds) as resp:
            return 200 <= resp.status < 400
    except Exception:
        return False


def _make_handler(config: AgentConfig):
    class HealthHandler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            if self.path in ("/health", "/healthz"):
                payload = {"status": "ok"}
                self._write_json(payload, 200)
                return
            if self.path in ("/ready", "/readyz"):
                payload = health_payload(config)
                code = 200 if payload["ready"] else 503
                self._write_json(payload, code)
                return
            self._write_json({"error": "not found"}, 404)

        def log_message(self, format: str, *args) -> None:  # noqa: A003
            return

        def _write_json(self, payload: dict[str, Any], status: int) -> None:
            body = json.dumps(payload, indent=2, sort_keys=True).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    return HealthHandler
