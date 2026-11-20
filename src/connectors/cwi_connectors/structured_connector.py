from __future__ import annotations

import csv
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Iterable, List

from .fixture_loader import resolve_fixture_path
from .framework import Connector, ConnectorContext, ConnectorRunResult
from .state_store import ConnectorState
from .versioning import CONNECTOR_SDK_VERSION


@dataclass(frozen=True)
class StructuredSource:
    source_id: str
    priority: int | None = None


class StructuredConnector(Connector):
    name = "structured"
    sdk_version = CONNECTOR_SDK_VERSION

    def discover(self) -> Iterable[StructuredSource]:
        raise NotImplementedError

    def fetch(self, source: StructuredSource, state: ConnectorState | None) -> Iterable[Dict[str, Any]]:
        raise NotImplementedError

    def normalize(self, source: StructuredSource, row: Dict[str, Any]) -> Dict[str, Any]:
        return row

    def checkpoint(self, state: ConnectorState | None, count: int) -> str | None:
        return f"rows:{count}"

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        items: List[Dict[str, Any]] = []
        for source in self.discover():
            for row in self.fetch(source, state):
                items.append(self.normalize(source, row))
        next_cursor = self.checkpoint(state, len(items))
        return ConnectorRunResult(items=items, next_cursor=next_cursor)


@dataclass(frozen=True)
class CsvStructuredConfig:
    source_id: str
    fixture_path: str | None = None
    path: str | None = None
    id_field: str = "doc_id"
    title_field: str = "title"
    content_field: str = "content"


class CsvStructuredConnector(StructuredConnector):
    name = "structured_csv"

    def __init__(self, config: CsvStructuredConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "CsvStructuredConnector":
        source_id = str(config.get("source_id") or "structured-csv")
        fixture_path = config.get("fixture_path")
        path = config.get("path")
        id_field = str(config.get("id_field") or "doc_id")
        title_field = str(config.get("title_field") or "title")
        content_field = str(config.get("content_field") or "content")
        return cls(
            CsvStructuredConfig(
                source_id=source_id,
                fixture_path=fixture_path,
                path=path,
                id_field=id_field,
                title_field=title_field,
                content_field=content_field,
            ),
            auth=auth,
        )

    def discover(self) -> Iterable[StructuredSource]:
        return [StructuredSource(source_id=self.config.source_id)]

    def fetch(self, source: StructuredSource, state: ConnectorState | None) -> Iterable[Dict[str, Any]]:
        path = self._resolve_path()
        if not path:
            raise ValueError("structured_csv connector requires path or fixture_path")
        with path.open("r", encoding="utf-8") as handle:
            reader = csv.DictReader(handle)
            return list(reader)

    def normalize(self, source: StructuredSource, row: Dict[str, Any]) -> Dict[str, Any]:
        doc_id = row.get(self.config.id_field) or row.get("id") or ""
        title = row.get(self.config.title_field) or row.get("name") or ""
        content = row.get(self.config.content_field) or row.get("body") or ""
        metadata = {key: value for key, value in row.items() if key not in {self.config.id_field, self.config.title_field, self.config.content_field}}
        metadata.update({"source_id": source.source_id})
        return {
            "doc_id": str(doc_id),
            "title": str(title),
            "content": str(content),
            "source": "structured-csv",
            "metadata": metadata,
        }

    def _resolve_path(self):
        if self.config.path:
            raw = Path(self.config.path)
            if raw.is_absolute() or raw.exists():
                return raw
            return resolve_fixture_path(self.config.path)
        if self.config.fixture_path:
            return resolve_fixture_path(self.config.fixture_path)
        return None
