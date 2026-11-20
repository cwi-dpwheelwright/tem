from __future__ import annotations

import json
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Callable, Dict

from .metrics import MetricsRegistry


@dataclass(frozen=True)
class AdminHandler:
    status: Callable[[], Dict[str, Any]]
    run: Callable[[Dict[str, Any] | None], Dict[str, Any]]


def run_health_server(
    host: str,
    port: int,
    metrics: MetricsRegistry,
    readiness_check: Callable[[], bool] | None = None,
    admin_handler: AdminHandler | None = None,
    admin_token: str | None = None,
) -> ThreadingHTTPServer:
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            if self.path == "/health":
                self._write(200, "ok")
                return
            if self.path == "/ready":
                ready = readiness_check() if readiness_check else True
                self._write(200 if ready else 503, "ready" if ready else "not ready")
                return
            if self.path == "/metrics":
                payload = metrics.render_prometheus()
                self.send_response(200)
                self.send_header("Content-Type", "text/plain; version=0.0.4")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload.encode("utf-8"))
                return
            if self.path == "/admin/status":
                if not admin_handler or not admin_token:
                    self._write(404, "not found")
                    return
                if not self._authorized(admin_token):
                    self._write(403, "forbidden")
                    return
                payload = json.dumps(admin_handler.status()).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)
                return
            self._write(404, "not found")

        def do_POST(self) -> None:
            if self.path != "/admin/run":
                self._write(404, "not found")
                return
            if not admin_handler or not admin_token:
                self._write(404, "not found")
                return
            if not self._authorized(admin_token):
                self._write(403, "forbidden")
                return
            length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(length) if length > 0 else b""
            try:
                payload = json.loads(body.decode("utf-8")) if body else None
            except json.JSONDecodeError:
                self._write(400, "invalid json")
                return
            response = admin_handler.run(payload)
            data = json.dumps(response).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, format: str, *args: object) -> None:  # noqa: A003
            return

        def _write(self, code: int, body: str) -> None:
            payload = body.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", "text/plain")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def _authorized(self, token: str) -> bool:
            auth = self.headers.get("Authorization", "")
            if auth.startswith("Bearer "):
                return auth[len("Bearer ") :].strip() == token
            alt = self.headers.get("X-Admin-Token", "")
            return bool(alt) and alt.strip() == token

    server = ThreadingHTTPServer((host, port), Handler)
    return server
