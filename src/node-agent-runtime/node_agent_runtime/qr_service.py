"""VPN-restricted QR service for home-node linking."""

from __future__ import annotations

from html import escape
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import ipaddress
import json
from io import BytesIO
from typing import Any
from urllib.parse import urlsplit

import qrcode
from qrcode.image.svg import SvgPathImage

from .config import AgentConfig


def serve_qr(config: AgentConfig, host: str, port: int) -> None:
    handler = _make_handler(config)
    server = ThreadingHTTPServer((host, port), handler)
    server.serve_forever()


def build_qr_payload(config: AgentConfig) -> dict[str, Any]:
    qr_config = config.qr_service
    return {
        "node_id": config.identity.node_id,
        "identifier_version": qr_config.identifier_version,
    }


def is_allowed_client(client_ip: str | None, allowed_networks: tuple[str, ...]) -> bool:
    normalized = _normalize_ip(client_ip)
    if not normalized:
        return False

    try:
        address = ipaddress.ip_address(normalized)
    except ValueError:
        return False
    for network in allowed_networks:
        if address in ipaddress.ip_network(network, strict=False):
            return True
    return False


def render_qr_svg(payload: dict[str, Any]) -> str:
    qr = qrcode.QRCode(box_size=10, border=4)
    qr.add_data(_payload_json(payload))
    qr.make(fit=True)
    image = qr.make_image(image_factory=SvgPathImage)
    buffer = BytesIO()
    image.save(buffer)
    return buffer.getvalue().decode("utf-8")


def render_qr_page(payload: dict[str, Any]) -> str:
    payload_json = json.dumps(payload, indent=2, sort_keys=True)
    return f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>CWI Home Node QR</title>
  <style>
    :root {{
      color-scheme: light;
      font-family: "Segoe UI", sans-serif;
      background: #eef4f1;
      color: #143227;
    }}
    body {{
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      background:
        radial-gradient(circle at top, rgba(93, 166, 124, 0.18), transparent 40%),
        linear-gradient(180deg, #f6fbf8 0%, #e6f1eb 100%);
    }}
    main {{
      width: min(92vw, 860px);
      background: rgba(255, 255, 255, 0.95);
      border: 1px solid rgba(20, 50, 39, 0.12);
      border-radius: 24px;
      box-shadow: 0 24px 60px rgba(20, 50, 39, 0.12);
      padding: 32px;
    }}
    h1 {{
      margin: 0 0 8px;
      font-size: 1.8rem;
    }}
    p {{
      margin: 0 0 16px;
      line-height: 1.5;
      color: #355245;
    }}
    .layout {{
      display: grid;
      gap: 24px;
      align-items: start;
    }}
    .qr-card {{
      display: grid;
      justify-items: center;
      gap: 12px;
      padding: 24px;
      border-radius: 20px;
      background: #f8fcfa;
      border: 1px solid rgba(20, 50, 39, 0.08);
    }}
    .qr-card img {{
      width: min(100%, 320px);
      height: auto;
    }}
    code, pre {{
      font-family: "Cascadia Code", monospace;
    }}
    pre {{
      margin: 0;
      padding: 18px;
      border-radius: 16px;
      background: #0f2018;
      color: #e4f8ec;
      overflow: auto;
    }}
    .links {{
      display: flex;
      gap: 16px;
      flex-wrap: wrap;
    }}
    a {{
      color: #0f6b42;
      font-weight: 600;
      text-decoration: none;
    }}
    a:hover {{
      text-decoration: underline;
    }}
    @media (min-width: 760px) {{
      .layout {{
        grid-template-columns: minmax(280px, 340px) 1fr;
      }}
    }}
  </style>
</head>
<body>
  <main>
    <h1>Home Node Link QR</h1>
    <p>
      Scan this QR code from <strong>Account Settings</strong> in the GPT site to
      link the signed-in user to this home node. The QR only advertises the
      immutable node identity. If the node is still unclaimed, the
      first successful scan will claim it before linking.
    </p>
    <div class="layout">
      <section class="qr-card">
        <img src="/qr.svg" alt="Home node link QR code">
        <div class="links">
          <a href="/qr.svg">Download SVG</a>
          <a href="/payload.json">View JSON payload</a>
        </div>
      </section>
      <section>
        <p>The QR payload emitted by this service:</p>
        <pre>{escape(payload_json)}</pre>
      </section>
    </div>
  </main>
</body>
</html>
"""


def _make_handler(config: AgentConfig):
    class QrHandler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            path = urlsplit(self.path).path
            client_ip = self.client_address[0] if self.client_address else None

            if not is_allowed_client(client_ip, config.qr_service.allowed_networks):
                self._write_json({"error": "forbidden"}, 403)
                return

            if path in ("/health", "/healthz"):
                self._write_json({"status": "ok"}, 200)
                return

            payload = build_qr_payload(config)
            if path in ("/", "/index.html"):
                self._write_text(render_qr_page(payload), 200, "text/html; charset=utf-8")
                return
            if path in ("/payload", "/payload.json"):
                self._write_json(payload, 200)
                return
            if path == "/qr.svg":
                self._write_text(render_qr_svg(payload), 200, "image/svg+xml; charset=utf-8")
                return

            self._write_json({"error": "not found"}, 404)

        def log_message(self, format: str, *args) -> None:  # noqa: A003
            return

        def _write_json(self, payload: dict[str, Any], status: int) -> None:
            body = json.dumps(payload, indent=2, sort_keys=True).encode("utf-8")
            self._write_bytes(body, status, "application/json")

        def _write_text(self, body: str, status: int, content_type: str) -> None:
            self._write_bytes(body.encode("utf-8"), status, content_type)

        def _write_bytes(self, body: bytes, status: int, content_type: str) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

    return QrHandler


def _payload_json(payload: dict[str, Any]) -> str:
    return json.dumps(payload, separators=(",", ":"), sort_keys=True)


def _normalize_ip(address: str | None) -> str | None:
    if not address:
        return None
    return address.split("%", 1)[0].strip()
