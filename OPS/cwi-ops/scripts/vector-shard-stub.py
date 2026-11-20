#!/usr/bin/env python3
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json


class Handler(BaseHTTPRequestHandler):
    def _write(self, code: int, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path == "/collections":
            self._write(200, {"collections": []})
            return
        if self.path == "/":
            self._write(200, {"status": "ok"})
            return
        self._write(404, {"error": "not_found"})

    def do_POST(self) -> None:
        # Minimal Qdrant search stub used by orchestrator.
        if self.path.startswith("/collections/") and self.path.endswith("/points/search"):
            self._write(200, {"result": []})
            return
        self._write(404, {"error": "not_found"})

    def log_message(self, format, *args) -> None:
        return


if __name__ == "__main__":
    server = ThreadingHTTPServer(("0.0.0.0", 6333), Handler)
    server.serve_forever()
