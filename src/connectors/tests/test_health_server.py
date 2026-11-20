import json
import threading
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from cwi_connectors.health_server import AdminHandler, run_health_server
from cwi_connectors.metrics import MetricsRegistry


def test_health_and_ready_endpoints():
    metrics = MetricsRegistry()
    server = run_health_server("127.0.0.1", 0, metrics, readiness_check=lambda: True)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    host, port = server.server_address
    with urlopen(f"http://{host}:{port}/health") as response:
        assert response.status == 200
    with urlopen(f"http://{host}:{port}/ready") as response:
        assert response.status == 200
    server.shutdown()


def test_admin_endpoints_require_token():
    metrics = MetricsRegistry()
    admin = AdminHandler(status=lambda: {"ok": True}, run=lambda _: {"ok": True})
    server = run_health_server("127.0.0.1", 0, metrics, admin_handler=admin, admin_token="secret")
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    host, port = server.server_address
    try:
        with urlopen(f"http://{host}:{port}/admin/status"):
            pass
    except HTTPError as exc:
        assert exc.code == 403
    request = Request(
        f"http://{host}:{port}/admin/status",
        headers={"Authorization": "Bearer secret"},
    )
    with urlopen(request) as response:
        assert response.status == 200
        payload = json.loads(response.read().decode("utf-8"))
        assert payload["ok"] is True
    run_request = Request(
        f"http://{host}:{port}/admin/run",
        data=json.dumps({"connector_id": "demo"}).encode("utf-8"),
        headers={"Authorization": "Bearer secret", "Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(run_request) as response:
        assert response.status == 200
    server.shutdown()
