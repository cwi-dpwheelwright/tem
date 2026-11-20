from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Iterable, Protocol

from .state_store import ConnectorState, StateStore

@dataclass(frozen=True)
class ConnectorContext:
    tenant_id: str
    workspace_id: str
    connector_id: str


class Connector(Protocol):
    name: str
    sdk_version: str

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "Connector":
        ...

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> "ConnectorRunResult":
        ...


@dataclass(frozen=True)
class ConnectorRunResult:
    items: Iterable[Dict[str, Any]]
    next_cursor: str | None


class ConnectorScheduler:
    def __init__(self, store: StateStore) -> None:
        self.store = store

    def run_once(self, connector: Connector, context: ConnectorContext) -> ConnectorRunResult:
        state = self.store.load(context.connector_id)
        result = connector.run(context, state)
        self.store.save(context.connector_id, result.next_cursor)
        return result
