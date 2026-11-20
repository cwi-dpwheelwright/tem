import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

import pytest

from cwi_etl.consent_remote import RemoteConsentClient, RemoteConsentConfig
from cwi_etl.operator_memory import ConsentMissingError


class _ConsentHandler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path != "/consent/latest":
            self.send_response(404)
            self.end_headers()
            return
        query = parse_qs(parsed.query)
        operator_id = (query.get("operator_id") or [""])[0]
        if operator_id == "missing":
            self.send_response(404)
            self.end_headers()
            return
        payload = {
            "consent": {
                "granted": True,
                "granted_utc": "2026-02-02T17:55:00Z",
                "source": "consent-service",
            }
        }
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format: str, *args) -> None:
        return


def _start_server() -> ThreadingHTTPServer:
    server = ThreadingHTTPServer(("127.0.0.1", 0), _ConsentHandler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server


@pytest.mark.integration
def test_remote_consent_client_fetches() -> None:
    server = _start_server()
    try:
        host, port = server.server_address
        client = RemoteConsentClient(
            RemoteConsentConfig(base_url=f"http://{host}:{port}", path="/consent/latest")
        )
        consent = client.get_latest_consent(
            tenant_id="tenant-demo",
            operator_id="op-217",
            observed_utc="2026-02-02T18:10:00Z",
        )
        assert consent is not None
        assert consent.granted is True
        assert consent.granted_utc == "2026-02-02T17:55:00Z"
        assert consent.source == "consent-service"
    finally:
        server.shutdown()


@pytest.mark.integration
def test_remote_consent_client_missing_returns_none() -> None:
    server = _start_server()
    try:
        host, port = server.server_address
        client = RemoteConsentClient(
            RemoteConsentConfig(base_url=f"http://{host}:{port}", path="/consent/latest")
        )
        consent = client.get_latest_consent(
            tenant_id="tenant-demo",
            operator_id="missing",
            observed_utc="2026-02-02T18:10:00Z",
        )
        assert consent is None
        with pytest.raises(ConsentMissingError):
            client.get_consent_or_raise(
                tenant_id="tenant-demo",
                operator_id="missing",
                observed_utc="2026-02-02T18:10:00Z",
            )
    finally:
        server.shutdown()
