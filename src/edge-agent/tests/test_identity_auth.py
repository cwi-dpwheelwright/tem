import json
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

from edge_agent.config import IdentityAuthConfig, load_config
from edge_agent.token_provider import IdentityTokenProvider


def test_load_config_uses_identity_credentials(tmp_path: Path, monkeypatch) -> None:
    config_path = tmp_path / "edge-agent.yaml"
    config_path.write_text(
        "\n".join(
            [
                "gateway_url: \"https://gateway.local\"",
                "tenant_id: \"tenant-1\"",
                "poll_interval_seconds: 5",
                f"buffer_path: \"{(tmp_path / 'buffer.jsonl').as_posix()}\"",
            ]
        ),
        encoding="utf-8",
    )

    monkeypatch.setenv("IDENTITY_URL", "http://identity.local")
    monkeypatch.setenv("CWI_EDGE_AGENT_IDENTITY_USERNAME", "edge-agent")
    monkeypatch.setenv("CWI_EDGE_AGENT_IDENTITY_PASSWORD", "secret")

    config, auth_source = load_config(config_path)

    assert auth_source == "identity"
    assert config.identity_auth.login_url == "http://identity.local/auth/login"
    assert config.identity_auth.refresh_url == "http://identity.local/auth/refresh"
    assert config.identity_auth.tenant_id == "tenant-1"
    assert config.identity_auth.username == "edge-agent"


def test_identity_token_provider_logs_in_and_refreshes() -> None:
    calls: list[tuple[str, dict[str, object]]] = []
    state = {"refresh": 0}

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(length).decode("utf-8"))
            calls.append((self.path, payload))

            if self.path == "/auth/login":
                response = {
                    "access_token": "access-login",
                    "refresh_token": "refresh-1",
                    "expires_utc": (
                        datetime.now(timezone.utc) + timedelta(seconds=30)
                    ).isoformat().replace("+00:00", "Z"),
                }
            elif self.path == "/auth/refresh":
                state["refresh"] += 1
                response = {
                    "access_token": f"access-refresh-{state['refresh']}",
                    "refresh_token": f"refresh-{state['refresh'] + 1}",
                    "expires_utc": (
                        datetime.now(timezone.utc) + timedelta(hours=1)
                    ).isoformat().replace("+00:00", "Z"),
                }
            else:
                self.send_response(404)
                self.end_headers()
                return

            body = json.dumps(response).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, format, *args) -> None:  # noqa: A003
            return

    server = HTTPServer(("localhost", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    try:
        base_url = f"http://localhost:{server.server_port}"
        provider = IdentityTokenProvider(
            IdentityAuthConfig(
                login_url=f"{base_url}/auth/login",
                refresh_url=f"{base_url}/auth/refresh",
                tenant_id="tenant-1",
                username="edge-agent",
                password="secret",
                refresh_margin_seconds=60,
            ),
            timeout_seconds=2.0,
        )

        first = provider.get_token()
        second = provider.get_token()

        assert first == "access-login"
        assert second == "access-refresh-1"
        assert calls[0] == (
            "/auth/login",
            {"tenant_id": "tenant-1", "username": "edge-agent", "password": "secret"},
        )
        assert calls[1] == (
            "/auth/refresh",
            {"refresh_token": "refresh-1"},
        )
    finally:
        server.shutdown()
        server.server_close()
