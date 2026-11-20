import argparse
import json
import os
import time
import urllib.error
import urllib.request
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse


def _env_flag(name: str, default: bool = False) -> bool:
    value = os.environ.get(name, "")
    if not value:
        return default
    return value.lower() in ("1", "true", "yes", "on")


def _probe(upstream_url: str, health_path: str, timeout_seconds: float) -> tuple[bool, int | None, float, str | None]:
    url = f"{upstream_url.rstrip('/')}{health_path}"
    start = time.perf_counter()
    status = None
    error = None
    ok = False
    try:
        req = urllib.request.Request(url, method="GET")
        with urllib.request.urlopen(req, timeout=timeout_seconds) as resp:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
            status = resp.getcode()
            ok = 200 <= status < 300
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError, OSError, ValueError) as exc:
        error = str(exc)
        ok = False
    latency_ms = (time.perf_counter() - start) * 1000.0
    return ok, status, latency_ms, error


def _labels() -> str:
    raw = os.environ.get("MODEL_METRICS_LABELS", "").strip()
    return f"{{{raw}}}" if raw else ""


def _validate_http_url(value: str, label: str) -> None:
    parsed = urlparse(value)
    if parsed.scheme not in ("http", "https") or not parsed.netloc:
        raise SystemExit(f"{label} must be an http(s) URL")


class HealthHandler(BaseHTTPRequestHandler):
    upstream_url: str = ""
    health_path: str = "/api/tags"
    timeout_seconds: float = 2.0
    enable_logs: bool = False

    def _send_json(self, status: int, payload: dict) -> None:
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=True).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_text(self, status: int, payload: str, content_type: str) -> None:
        body = payload.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _handle_health(self) -> None:
        ok, status, latency_ms, error = _probe(
            self.upstream_url, self.health_path, self.timeout_seconds
        )
        payload = {
            "ok": ok,
            "upstream_status": status,
            "latency_ms": round(latency_ms, 1),
            "upstream_url": self.upstream_url,
        }
        if error:
            payload["error"] = error
        self._send_json(200 if ok else 503, payload)

    def _handle_metrics(self) -> None:
        ok, status, latency_ms, _ = _probe(
            self.upstream_url, self.health_path, self.timeout_seconds
        )
        labels = _labels()
        lines = [
            "# HELP cwi_model_worker_up Whether the model worker is reachable (1=yes).",
            "# TYPE cwi_model_worker_up gauge",
            f"cwi_model_worker_up{labels} {1 if ok else 0}",
            "# HELP cwi_model_worker_probe_latency_ms Model worker probe latency in milliseconds.",
            "# TYPE cwi_model_worker_probe_latency_ms gauge",
            f"cwi_model_worker_probe_latency_ms{labels} {latency_ms:.1f}",
        ]
        if status is not None:
            lines.extend(
                [
                    "# HELP cwi_model_worker_upstream_status Last upstream status code seen by probe.",
                    "# TYPE cwi_model_worker_upstream_status gauge",
                    f"cwi_model_worker_upstream_status{labels} {status}",
                ]
            )
        payload = "\n".join(lines) + "\n"
        self._send_text(200, payload, "text/plain; version=0.0.4")

    def do_GET(self) -> None:
        path = self.path.split("?", 1)[0]
        if path in ("/health", "/healthz", "/ready", "/readyz"):
            self._handle_health()
            return
        if path == "/metrics":
            self._handle_metrics()
            return
        self._send_text(404, "not found\n", "text/plain")

    def log_message(self, fmt: str, *args) -> None:  # pragma: no cover
        if self.enable_logs:
            super().log_message(fmt, *args)


def main() -> int:
    parser = argparse.ArgumentParser(description="Model worker health/metrics proxy")
    parser.add_argument("--host", default=os.environ.get("MODEL_HEALTH_BIND", "0.0.0.0"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("MODEL_HEALTH_PORT", "18092")))
    args = parser.parse_args()

    upstream_url = os.environ.get("MODEL_UPSTREAM_URL", "http://localhost:11434").strip()
    health_path = os.environ.get("MODEL_HEALTH_PATH", "/api/tags").strip()
    timeout_seconds = float(os.environ.get("MODEL_HEALTH_TIMEOUT_SECONDS", "2.0"))
    _validate_http_url(upstream_url, "MODEL_UPSTREAM_URL")

    HealthHandler.upstream_url = upstream_url
    HealthHandler.health_path = health_path
    HealthHandler.timeout_seconds = timeout_seconds
    HealthHandler.enable_logs = _env_flag("MODEL_HEALTH_LOGS", False)

    server = HTTPServer((args.host, args.port), HealthHandler)
    server.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
