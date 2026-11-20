import os
import subprocess
import sys
import time
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from threading import Thread

import pytest


pytestmark = pytest.mark.simulation


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _edge_agent_root() -> Path:
    override = os.environ.get("CWI_EDGE_AGENT_ROOT")
    if override:
        return Path(override)
    return _repo_root() / "src" / "edge-agent"


def _python_env(edge_root: Path) -> dict:
    env = os.environ.copy()
    env["PYTHONPATH"] = str(edge_root)
    return env


def _run_edge_agent(*args: str, cwd: Path, env: dict, duration_seconds: float = 2.0) -> None:
    proc = subprocess.Popen(
        [sys.executable, "-m", "edge_agent", *args],
        cwd=str(cwd),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(duration_seconds)
    proc.terminate()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.kill()


def _run_import(sample: Path, mapping: Path, tenant_id: str, buffer_path: Path, cwd: Path, env: dict) -> None:
    subprocess.run(
        [
            sys.executable,
            "-m",
            "edge_agent",
            "import-file",
            "--input",
            str(sample),
            "--mapping",
            str(mapping),
            "--tenant-id",
            tenant_id,
            "--buffer",
            str(buffer_path),
        ],
        cwd=str(cwd),
        env=env,
        check=True,
    )


class _IngestHandler(BaseHTTPRequestHandler):
    def do_POST(self) -> None:
        if self.path != "/api/ingest/telemetry":
            self.send_response(404)
            self.end_headers()
            return
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        self.server.requests.append(body)  # type: ignore[attr-defined]
        self.send_response(200)
        self.end_headers()

    def log_message(self, *_args) -> None:
        return


def _start_server() -> tuple[HTTPServer, Thread]:
    server = HTTPServer(("127.0.0.1", 0), _IngestHandler)
    server.requests = []  # type: ignore[attr-defined]
    thread = Thread(target=server.serve_forever, daemon=True)
    thread.start()
    return server, thread


def test_edge_agent_offline_buffering(tmp_path: Path) -> None:
    edge_root = _edge_agent_root()
    if not edge_root.exists():
        pytest.skip("edge agent root not found")

    sample = edge_root / "configs" / "telemetry_sample.csv"
    mapping = edge_root / "configs" / "telemetry_tag_map.example.yaml"
    buffer_path = tmp_path / "buffer.jsonl"
    config_path = tmp_path / "edge-agent.yaml"

    config_path.write_text(
        "\n".join(
            [
                "gateway_url: \"http://127.0.0.1:59999\"",
                "tenant_id: \"tenant-1\"",
                "poll_interval_seconds: 1",
                f"buffer_path: \"{buffer_path.as_posix()}\"",
                "jwt: \"test-token\"",
                "survival:",
                "  enabled: false",
            ]
        ),
        encoding="utf-8",
    )

    env = _python_env(edge_root)
    _run_import(sample, mapping, "tenant-1", buffer_path, edge_root, env)
    _run_edge_agent("run", "--config", str(config_path), "--max-batch", "5", cwd=edge_root, env=env)

    cursor = buffer_path.with_suffix(".cursor")
    assert buffer_path.exists()
    if cursor.exists():
        assert cursor.read_text(encoding="utf-8").strip() in ("", "0")


def test_edge_agent_replay_when_online(tmp_path: Path) -> None:
    edge_root = _edge_agent_root()
    if not edge_root.exists():
        pytest.skip("edge agent root not found")

    sample = edge_root / "configs" / "telemetry_sample.csv"
    mapping = edge_root / "configs" / "telemetry_tag_map.example.yaml"
    buffer_path = tmp_path / "buffer.jsonl"
    config_path = tmp_path / "edge-agent.yaml"

    server, thread = _start_server()
    host, port = server.server_address
    config_path.write_text(
        "\n".join(
            [
                f"gateway_url: \"http://{host}:{port}\"",
                "tenant_id: \"tenant-1\"",
                "poll_interval_seconds: 1",
                f"buffer_path: \"{buffer_path.as_posix()}\"",
                "jwt: \"test-token\"",
                "survival:",
                "  enabled: false",
            ]
        ),
        encoding="utf-8",
    )

    env = _python_env(edge_root)
    _run_import(sample, mapping, "tenant-1", buffer_path, edge_root, env)
    _run_edge_agent(
        "run",
        "--config",
        str(config_path),
        "--max-batch",
        "5",
        cwd=edge_root,
        env=env,
        duration_seconds=5.0,
    )

    server.shutdown()
    thread.join(timeout=5)

    cursor = buffer_path.with_suffix(".cursor")
    assert cursor.exists()
    assert cursor.read_text(encoding="utf-8").strip() not in ("", "0")
    assert getattr(server, "requests", [])
