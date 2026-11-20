#!/usr/bin/env python
import argparse
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

from runtime_critic import VERSION, evaluate


MAX_BODY_BYTES = 1024 * 1024


def cmd_serve(args: argparse.Namespace) -> int:
    host = args.host
    port = args.port

    class Handler(BaseHTTPRequestHandler):
        def _write_json(self, status_code: int, payload: dict[str, Any]) -> None:
            body = (json.dumps(payload) + "\n").encode("utf-8")
            self.send_response(status_code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:  # noqa: N802
            if self.path == "/health":
                self._write_json(
                    200,
                    {
                        "status": "healthy",
                        "service": "cwi-critic-node",
                        "runtime_critic_version": VERSION,
                    },
                )
                return
            self._write_json(404, {"error": "not_found"})

        def do_POST(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            if path != "/runtime/critic/observe":
                self._write_json(404, {"error": "not_found"})
                return

            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                self._write_json(400, {"status": "rejected", "reason": "invalid_content_length"})
                return

            if length <= 0:
                self._write_json(400, {"status": "rejected", "reason": "empty_body"})
                return
            if length > MAX_BODY_BYTES:
                self._write_json(413, {"status": "rejected", "reason": "payload_too_large"})
                return

            try:
                payload = json.loads(self.rfile.read(length).decode("utf-8"))
            except json.JSONDecodeError:
                self._write_json(400, {"status": "rejected", "reason": "invalid_json"})
                return

            if not isinstance(payload, dict):
                self._write_json(400, {"status": "rejected", "reason": "invalid_payload"})
                return

            self._write_json(200, evaluate(payload))

        def log_message(self, format: str, *args: Any) -> None:
            return

    server = ThreadingHTTPServer((host, port), Handler)
    print(f"critic-node listening on {host}:{port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="CWI Critic node")
    sub = parser.add_subparsers(dest="command", required=True)
    serve = sub.add_parser("serve")
    serve.add_argument("--host", default="0.0.0.0")
    serve.add_argument("--port", type=int, default=int(os.getenv("CRITIC_NODE_HTTP_PORT", "8086")))
    serve.set_defaults(func=cmd_serve)
    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
