import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

from edge_agent.metrics import LinkMetricsInterface, LinkMetricsQuality
from edge_agent.metrics_exporter import MetricsExporter, MetricsExporterConfig


def test_metrics_exporter_posts_payload(tmp_path) -> None:
    event = threading.Event()
    received: dict[str, object] = {}

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0"))
            body = self.rfile.read(length).decode("utf-8")
            received["path"] = self.path
            received["auth"] = self.headers.get("Authorization")
            received["payload"] = json.loads(body)
            self.send_response(200)
            self.end_headers()
            event.set()

        def log_message(self, format, *args) -> None:  # noqa: A003 - signature required by base class
            return

    server = HTTPServer(("localhost", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    try:
        port = server.server_port
        queue_path = tmp_path / "metrics-buffer.jsonl"
        config = MetricsExporterConfig(
            endpoints=(f"http://localhost:{port}",),
            node_id="edge-node-1",
            queue_path=str(queue_path),
            queue_max_items=10,
            queue_max_bytes=10240,
            queue_drop_policy="drop_oldest",
            timeout_seconds=2.0,
            max_batch=5,
            backoff_base_seconds=0.5,
            backoff_max_seconds=2.0,
            failure_threshold=2,
            recovery_seconds=1.0,
        )
        exporter = MetricsExporter(config, token_provider=lambda: "test-token")
        observed = "2026-02-02T00:00:00Z"
        interfaces = [
            LinkMetricsInterface(
                interface_id="wifi0",
                interface_type="wifi",
                state="up",
                observed_utc=observed,
                rx_mbps=12.5,
                tx_mbps=7.5,
                mtu=1500,
                quality=LinkMetricsQuality(
                    latency_ms=10.0,
                    jitter_ms=1.2,
                    loss_ratio=0.01,
                    throughput_mbps=50.0,
                    rssi_dbm=-45.0,
                    snr_db=25.0,
                ),
            )
        ]
        assert exporter.enqueue(interfaces, observed)
        exporter.flush()

        assert event.wait(2.0)
        payload = received["payload"]
        assert received["path"] == "/api/transport/link-metrics"
        assert received["auth"] == "Bearer test-token"
        assert payload["node_id"] == "edge-node-1"
        assert payload["observed_utc"] == observed
        assert payload["sequence"] == 1
        assert payload["interfaces"][0]["interface_id"] == "wifi0"
        assert payload["payload"]["interfaces"][0]["quality"]["latency_ms"] == 10.0
    finally:
        server.shutdown()
        server.server_close()
