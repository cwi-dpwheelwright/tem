from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Dict, List

from .telemetry_importer import (
    TelemetryIngestPayload,
    TelemetrySignal,
    TelemetryTagMap,
    TelemetryWindow,
)

try:
    from asyncua import Client  # type: ignore
except Exception:  # pragma: no cover - optional dependency
    Client = None


@dataclass(frozen=True)
class OpcUaSample:
    timestamp: str
    values: Dict[str, float]


class OpcUaCollector:
    def __init__(self, endpoint: str, tag_map: TelemetryTagMap) -> None:
        self.endpoint = endpoint
        self.tag_map = tag_map

    async def collect_once(self) -> OpcUaSample:
        if Client is None:
            raise RuntimeError("asyncua is not installed. Add dependency 'asyncua' to use OPC UA collector.")
        values: Dict[str, float] = {}
        async with Client(url=self.endpoint) as client:
            for mapping in self.tag_map.signals:
                if not mapping.opcua_node:
                    continue
                node = client.get_node(mapping.opcua_node)
                value = await node.read_value()
                try:
                    values[mapping.tag] = float(value)
                except (TypeError, ValueError):
                    continue
        timestamp = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        return OpcUaSample(timestamp=timestamp, values=values)


def build_payload_from_opcua(
    tenant_id: str,
    sample: OpcUaSample,
    tag_map: TelemetryTagMap,
) -> TelemetryIngestPayload:
    signals: List[TelemetrySignal] = []
    for mapping in tag_map.signals:
        if mapping.tag not in sample.values:
            continue
        signals.append(
            TelemetrySignal(
                tag=mapping.tag,
                unit=mapping.unit,
                series=[sample.values[mapping.tag]],
            )
        )

    window = TelemetryWindow(start=sample.timestamp, end=sample.timestamp)
    metadata = {
        "source": "opcua",
        "timestamp_column": tag_map.timestamp_column,
        "mapped_signals": [mapping.tag for mapping in tag_map.signals if mapping.opcua_node],
    }
    return TelemetryIngestPayload(
        tenant_id=tenant_id,
        window=window,
        signals=signals,
        metadata=metadata,
    )
