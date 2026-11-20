import json
from pathlib import Path

from edge_agent.telemetry_importer import import_telemetry_file


def _write(path: Path, content: str) -> Path:
    path.write_text(content, encoding="utf-8")
    return path


def test_import_csv_builds_payload(tmp_path: Path) -> None:
    mapping = _write(
        tmp_path / "map.yaml",
        "\n".join(
            [
                "timestamp_column: ts",
                "signals:",
                "  - source: temp_c",
                "    tag: temperature",
                "    unit: C",
                "  - source: pressure_kpa",
                "    tag: pressure",
                "    unit: kPa",
            ]
        ),
    )
    csv_path = _write(
        tmp_path / "data.csv",
        "\n".join(
            [
                "ts,temp_c,pressure_kpa",
                "2026-01-01T00:00:00Z,10.5,101.3",
                "2026-01-01T00:01:00Z,11.0,101.2",
            ]
        ),
    )
    payload = import_telemetry_file(csv_path, mapping, "tenant-1")
    payload_dict = payload.to_dict()
    assert payload_dict["tenant_id"] == "tenant-1"
    assert payload_dict["window"]["start"] == "2026-01-01T00:00:00Z"
    assert payload_dict["window"]["end"] == "2026-01-01T00:01:00Z"
    assert len(payload_dict["signals"]) == 2
    assert payload_dict["metadata"]["idempotency_key"]


def test_import_json_builds_payload(tmp_path: Path) -> None:
    mapping = _write(
        tmp_path / "map.yaml",
        "\n".join(
            [
                "timestamp_column: timestamp",
                "signals:",
                "  - source: flow_rate",
                "    tag: flow",
                "    unit: L/s",
            ]
        ),
    )
    rows = [
        {"timestamp": "2026-01-01T00:00:00Z", "flow_rate": 1.2},
        {"timestamp": "2026-01-01T00:05:00Z", "flow_rate": 1.3},
    ]
    json_path = _write(tmp_path / "data.json", json.dumps(rows))
    payload = import_telemetry_file(json_path, mapping, "tenant-2")
    payload_dict = payload.to_dict()
    assert payload_dict["window"]["start"] == "2026-01-01T00:00:00Z"
    assert payload_dict["window"]["end"] == "2026-01-01T00:05:00Z"
    assert payload_dict["signals"][0]["tag"] == "flow"
