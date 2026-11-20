import argparse
import json
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib import parse


class ActivationStatusHandler(BaseHTTPRequestHandler):
    def do_GET(self):  # noqa: N802
        parsed = parse.urlparse(self.path)
        if parsed.path != "/api/activation/status":
            self.send_response(404)
            self.end_headers()
            return

        params = parse.parse_qs(parsed.query)
        node_id = params.get("node_id", ["node-01"])[0] or "node-01"
        payload = {
            "node_id": node_id,
            "status": "active",
            "revoked_utc": None,
            "checked_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        }
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):  # noqa: A003
        return


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=18999)
    args = parser.parse_args()

    server = HTTPServer((args.host, args.port), ActivationStatusHandler)
    server.serve_forever()


if __name__ == "__main__":
    main()
