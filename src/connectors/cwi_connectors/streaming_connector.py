from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict

from .framework import Connector, ConnectorContext, ConnectorRunResult
from .state_store import ConnectorState
from .streaming import SimulatedStreamConfig, SimulatedStreamingAdapter, build_stream_documents
from .versioning import CONNECTOR_SDK_VERSION


@dataclass(frozen=True)
class SimulatedStreamingConnectorConfig:
    source_id: str
    total_events: int = 100
    events_per_poll: int = 25
    max_buffer_size: int = 50
    overflow_strategy: str = "drop_oldest"
    max_batch_size: int = 20


class SimulatedStreamingConnector(Connector):
    name = "stream_simulated"
    sdk_version = CONNECTOR_SDK_VERSION

    def __init__(self, config: SimulatedStreamingConnectorConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "SimulatedStreamingConnector":
        return cls(
            SimulatedStreamingConnectorConfig(
                source_id=str(config.get("source_id") or "simulated-stream"),
                total_events=int(config.get("total_events") or 100),
                events_per_poll=int(config.get("events_per_poll") or 25),
                max_buffer_size=int(config.get("max_buffer_size") or 50),
                overflow_strategy=str(config.get("overflow_strategy") or "drop_oldest"),
                max_batch_size=int(config.get("max_batch_size") or 20),
            ),
            auth=auth,
        )

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        cursor = 0
        if state and state.cursor:
            try:
                cursor = int(state.cursor)
            except ValueError:
                cursor = 0
        adapter = SimulatedStreamingAdapter(
            SimulatedStreamConfig(
                source_id=self.config.source_id,
                total_events=self.config.total_events,
                events_per_poll=self.config.events_per_poll,
                max_buffer_size=self.config.max_buffer_size,
                overflow_strategy=self.config.overflow_strategy,
                max_batch_size=self.config.max_batch_size,
            )
        )
        batch, next_cursor, metadata = adapter.poll(cursor)
        documents = build_stream_documents(batch, source=self.name, metadata=metadata)
        return ConnectorRunResult(items=documents, next_cursor=str(next_cursor))
