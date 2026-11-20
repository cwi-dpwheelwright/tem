from __future__ import annotations

import argparse
import threading

from .health_server import AdminHandler, run_health_server
from .runtime import RuntimeOptions, load_runtime


def main() -> int:
    parser = argparse.ArgumentParser(description="CWI connectors runtime")
    parser.add_argument("--config", required=True, help="Path to connectors YAML config")
    parser.add_argument("--once", action="store_true", help="Run once and exit")
    parser.add_argument("--health-port", type=int, default=8080, help="Health/metrics port")
    args = parser.parse_args()

    runtime = load_runtime(RuntimeOptions(config_path=args.config))
    admin_handler = None
    if runtime.config.admin_enabled and runtime.config.admin_token:
        def _run(payload):
            connector_id = payload.get("connector_id") if isinstance(payload, dict) else None
            return runtime.run_once(connector_id=connector_id)

        admin_handler = AdminHandler(status=runtime.admin_status, run=_run)
    server = run_health_server(
        "0.0.0.0",
        args.health_port,
        runtime.metrics,
        readiness_check=runtime.readiness_check,
        admin_handler=admin_handler,
        admin_token=runtime.config.admin_token if runtime.config.admin_enabled else None,
    )

    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()

    if args.once:
        runtime.run_once()
        return 0

    runtime.run_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
