from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path


def _write_config(config_path: Path, enrollment_url: str) -> None:
    config = {
        "coordinator_url": enrollment_url.rsplit("/api/membership/enroll", 1)[0],
        "enrollment_url": enrollment_url,
        "identity": {
            "node_id": "node-smoke-01",
            "private_key_path": "state/identity/private.pem",
            "public_key_path": "state/identity/public.pem",
            "persist_keys": True,
            "key_type": "ed25519",
        },
        "storage": {"state_dir": "state"},
        "enrollment": {
            "request_timeout_seconds": 5,
            "protocol_version": "1.0",
            "cert_fingerprint": "aabbcc",
        },
    }
    config_path.write_text(json.dumps(config, indent=2, sort_keys=True), encoding="utf-8")


class _EnrollmentHandler(BaseHTTPRequestHandler):
    server_version = "NodeAgentEnrollmentMock/1.0"

    def do_POST(self) -> None:  # noqa: N802
        length = int(self.headers.get("Content-Length", "0"))
        payload = self.rfile.read(length).decode("utf-8") if length else ""
        self.server.last_payload = payload
        self.server.last_path = self.path
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b"{\"status\":\"ok\"}")

    def log_message(self, format: str, *args) -> None:  # noqa: A003
        return


class _EnrollmentServer(HTTPServer):
    def __init__(self, server_address) -> None:
        super().__init__(server_address, _EnrollmentHandler)
        self.last_payload: str | None = None
        self.last_path: str | None = None


def _run_cli(repo_root: Path, config_path: Path, command: str) -> subprocess.CompletedProcess:
    env = os.environ.copy()
    env["PYTHONPATH"] = str(repo_root / "src" / "node-agent-runtime")
    return subprocess.run(
        [sys.executable, "-m", "node_agent_runtime.cli", command, "--config", str(config_path)],
        cwd=str(repo_root / "src" / "node-agent-runtime"),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        check=False,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", required=True)
    args = parser.parse_args()

    repo_root = Path(args.repo_root).resolve()

    with tempfile.TemporaryDirectory() as temp_dir:
        work_dir = Path(temp_dir)
        config_path = work_dir / "node-agent-smoke.json"

        server = _EnrollmentServer(("127.0.0.1", 0))
        host, port = server.server_address
        enrollment_url = f"http://{host}:{port}/api/membership/enroll"
        _write_config(config_path, enrollment_url)

        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()

        print("node-agent enrollment smoke")
        print(f"enrollment_url={enrollment_url}")
        print(f"config_path={config_path}")

        validate = _run_cli(repo_root, config_path, "validate-config")
        print("validate-config output:")
        print(validate.stdout.strip())
        if validate.returncode != 0:
            server.shutdown()
            return validate.returncode

        enroll = _run_cli(repo_root, config_path, "enroll")
        print("enroll output:")
        print(enroll.stdout.strip())
        if enroll.returncode != 0:
            server.shutdown()
            return enroll.returncode

        server.shutdown()
        thread.join(timeout=2)

        print("server received:")
        print(f"path={server.last_path}")
        payload = server.last_payload or ""
        print(payload.strip())

        try:
            parsed = json.loads(payload)
        except json.JSONDecodeError:
            parsed = None
        if parsed is None:
            print("error: enrollment payload not valid JSON")
            return 1

        expected_keys = {"node_id", "cert_fingerprint", "protocol_version"}
        if not expected_keys.issubset(parsed.keys()):
            print("error: enrollment payload missing expected fields")
            return 1

        print("result=ok")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
