from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List

from .fixture_loader import load_fixture
from .framework import Connector, ConnectorContext, ConnectorRunResult
from .state_store import ConnectorState
from .versioning import CONNECTOR_SDK_VERSION


@dataclass(frozen=True)
class JsonConnectorConfig:
    fixture_path: str | None = None
    record_path: str | None = None
    id_field: str = "id"
    title_field: str = "title"
    content_field: str = "body"


class JsonConnector(Connector):
    name = "json"
    sdk_version = CONNECTOR_SDK_VERSION

    def __init__(self, config: JsonConnectorConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "JsonConnector":
        fixture_path = config.get("path") or config.get("fixture_path")
        record_path = config.get("record_path") or "records"
        id_field = str(config.get("id_field") or "id")
        title_field = str(config.get("title_field") or "title")
        content_field = str(config.get("content_field") or "body")
        return cls(
            JsonConnectorConfig(
                fixture_path=fixture_path,
                record_path=record_path,
                id_field=id_field,
                title_field=title_field,
                content_field=content_field,
            ),
            auth=auth,
        )

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        records = self._load_records()
        items = [self._normalize(record) for record in records]
        return ConnectorRunResult(items=items, next_cursor=None)

    def _load_records(self) -> List[Dict[str, Any]]:
        payload = self._load_payload(self.config.fixture_path or "")
        records = self._resolve_path(payload, self.config.record_path)
        if not isinstance(records, list):
            return []
        return [record for record in records if isinstance(record, dict)]

    def _load_payload(self, path: str) -> Dict[str, Any]:
        if not path:
            raise ValueError("json connector requires path or fixture_path")
        raw_path = Path(path)
        if raw_path.is_absolute() or raw_path.exists():
            return json.loads(raw_path.read_text(encoding="utf-8-sig"))
        return load_fixture(path)

    def _normalize(self, record: Dict[str, Any]) -> Dict[str, Any]:
        doc_id = record.get(self.config.id_field) or record.get("doc_id") or record.get("id")
        title = record.get(self.config.title_field) or record.get("name") or record.get("title")
        content = record.get(self.config.content_field) or record.get("content") or record.get("body")
        metadata = {key: value for key, value in record.items() if key not in {self.config.id_field, self.config.title_field, self.config.content_field}}
        return {
            "doc_id": str(doc_id),
            "title": str(title),
            "content": str(content),
            "source": "json",
            "metadata": metadata,
        }

    @staticmethod
    def _resolve_path(payload: Dict[str, Any], record_path: str | None) -> Any:
        if not record_path:
            return payload
        current: Any = payload
        for part in record_path.split("."):
            if not isinstance(current, dict):
                return []
            current = current.get(part)
        return current
