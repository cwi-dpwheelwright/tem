from __future__ import annotations

import csv
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, Iterable, List, Tuple
import hashlib

import yaml


@dataclass(frozen=True)
class TelemetrySignal:
    tag: str
    unit: str
    series: List[float]


@dataclass(frozen=True)
class TelemetryWindow:
    start: str
    end: str


@dataclass(frozen=True)
class TelemetryIngestPayload:
    tenant_id: str
    window: TelemetryWindow
    signals: List[TelemetrySignal]
    metadata: Dict[str, Any]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "tenant_id": self.tenant_id,
            "window": {"start": self.window.start, "end": self.window.end},
            "signals": [
                {"tag": signal.tag, "unit": signal.unit, "series": signal.series}
                for signal in self.signals
            ],
            "metadata": self.metadata,
        }


@dataclass(frozen=True)
class TelemetryTagMapping:
    source: str
    tag: str
    unit: str
    opcua_node: str | None = None


@dataclass(frozen=True)
class TelemetryTagMap:
    timestamp_column: str
    signals: List[TelemetryTagMapping]

    @staticmethod
    def load(path: Path) -> "TelemetryTagMap":
        raw = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
        timestamp_column = raw.get("timestamp_column", "timestamp")
        signals = [
            TelemetryTagMapping(
                source=item["source"],
                tag=item["tag"],
                unit=item.get("unit", ""),
                opcua_node=item.get("opcua_node")
            )
            for item in raw.get("signals", [])
        ]
        if not signals:
            raise ValueError("telemetry_tag_map.yaml must define at least one signal mapping.")
        return TelemetryTagMap(timestamp_column=timestamp_column, signals=signals)


def import_telemetry_file(
    input_path: Path,
    tag_map_path: Path,
    tenant_id: str,
) -> TelemetryIngestPayload:
    tag_map = TelemetryTagMap.load(tag_map_path)
    if input_path.suffix.lower() == ".csv":
        rows = _read_csv_rows(input_path)
    elif input_path.suffix.lower() in {".json", ".jsonl"}:
        rows = _read_json_rows(input_path)
    else:
        raise ValueError("Unsupported file extension. Use .csv, .json, or .jsonl.")

    timestamps, signals = _extract_series(rows, tag_map)
    window = _build_window(timestamps)

    idempotency_key = _build_idempotency_key(tenant_id, window, signals)
    metadata = {
        "source_file": input_path.name,
        "row_count": len(rows),
        "timestamp_column": tag_map.timestamp_column,
        "mapped_signals": [mapping.tag for mapping in tag_map.signals],
        "idempotency_key": idempotency_key,
    }
    return TelemetryIngestPayload(
        tenant_id=tenant_id,
        window=window,
        signals=signals,
        metadata=metadata,
    )


def _read_csv_rows(path: Path) -> List[Dict[str, Any]]:
    with path.open(newline="", encoding="utf-8") as handle:
        reader = csv.DictReader(handle)
        return [row for row in reader if row]


def _read_json_rows(path: Path) -> List[Dict[str, Any]]:
    content = path.read_text(encoding="utf-8").strip()
    if not content:
        return []
    if path.suffix.lower() == ".jsonl":
        rows = [json.loads(line) for line in content.splitlines() if line.strip()]
        return [row for row in rows if isinstance(row, dict)]
    payload = json.loads(content)
    if isinstance(payload, dict) and "rows" in payload:
        payload = payload["rows"]
    if not isinstance(payload, list):
        raise ValueError("JSON telemetry input must be an array of objects or { rows: [...] }.")
    return [row for row in payload if isinstance(row, dict)]


def _extract_series(
    rows: Iterable[Dict[str, Any]],
    tag_map: TelemetryTagMap,
) -> Tuple[List[datetime], List[TelemetrySignal]]:
    timestamps: List[datetime] = []
    series_map: Dict[str, List[float]] = {mapping.tag: [] for mapping in tag_map.signals}

    for row in rows:
        timestamp_value = row.get(tag_map.timestamp_column)
        if timestamp_value is None:
            continue
        timestamp = _parse_timestamp(str(timestamp_value))
        timestamps.append(timestamp)
        for mapping in tag_map.signals:
            raw_value = row.get(mapping.source)
            if raw_value is None or raw_value == "":
                continue
            try:
                series_map[mapping.tag].append(float(raw_value))
            except ValueError:
                continue

    signals = [
        TelemetrySignal(tag=mapping.tag, unit=mapping.unit, series=series_map[mapping.tag])
        for mapping in tag_map.signals
    ]
    if not timestamps:
        raise ValueError("No telemetry timestamps found in input.")
    return timestamps, signals


def _parse_timestamp(value: str) -> datetime:
    normalized = value.replace("Z", "+00:00")
    dt = datetime.fromisoformat(normalized)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def _build_window(timestamps: List[datetime]) -> TelemetryWindow:
    start = min(timestamps)
    end = max(timestamps)
    return TelemetryWindow(
        start=start.isoformat().replace("+00:00", "Z"),
        end=end.isoformat().replace("+00:00", "Z"),
    )


def _build_idempotency_key(
    tenant_id: str,
    window: TelemetryWindow,
    signals: List[TelemetrySignal],
) -> str:
    digest = hashlib.sha256()
    digest.update(tenant_id.encode("utf-8"))
    digest.update(window.start.encode("utf-8"))
    digest.update(window.end.encode("utf-8"))
    for signal in sorted(signals, key=lambda s: s.tag):
        digest.update(signal.tag.encode("utf-8"))
        digest.update(signal.unit.encode("utf-8"))
        digest.update(str(len(signal.series)).encode("utf-8"))
    return digest.hexdigest()
