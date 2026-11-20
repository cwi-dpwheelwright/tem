import json
from pathlib import Path

from cwi_connectors.admin_monitoring import AdminMonitor


def test_admin_monitor_reads_state(tmp_path: Path) -> None:
    state_path = tmp_path / "state.json"
    state_path.write_text(
        json.dumps(
            {
                "conn-1": {"cursor": "cursor-1", "updated_utc": "2026-01-01T00:00:00Z"},
                "conn-2": {"cursor": None, "updated_utc": "2026-01-01T01:00:00Z"},
            }
        ),
        encoding="utf-8",
    )
    monitor = AdminMonitor(str(state_path))
    snapshot = monitor.snapshot()
    assert snapshot["count"] == 2
    assert snapshot["connectors"][0]["connector_id"] == "conn-1"
