from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Dict, List, Tuple

from .fixture_loader import load_fixture
from .framework import Connector, ConnectorContext, ConnectorState, ConnectorRunResult
from .auth import build_auth_headers, get_json
from .http_client import build_url
from .normalization import normalize_document
from .url_validation import validate_external_url
from .versioning import CONNECTOR_SDK_VERSION


@dataclass(frozen=True)
class SmartsheetConfig:
    sheet_id: str
    page_size: int = 100
    fixture_path: str | None = None
    api_base_url: str | None = None


class SmartsheetConnector(Connector):
    name = "smartsheet"
    sdk_version = CONNECTOR_SDK_VERSION

    def __init__(self, config: SmartsheetConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "SmartsheetConnector":
        sheet_id = str(config.get("sheet_id") or "sheet")
        page_size = int(config.get("page_size") or 100)
        fixture_path = config.get("fixture_path")
        api_base_url = config.get("api_base_url")
        if api_base_url:
            api_base_url = validate_external_url(str(api_base_url), "smartsheet.api_base_url")
        return cls(
            SmartsheetConfig(
                sheet_id=sheet_id,
                page_size=page_size,
                fixture_path=fixture_path,
                api_base_url=api_base_url,
            ),
            auth=auth,
        )

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        items, next_cursor = _fetch_rows(self.config, state.cursor if state else None, self.auth)
        normalized = [normalize_document(item) for item in items]
        return ConnectorRunResult(items=[doc.__dict__ for doc in normalized], next_cursor=next_cursor)


def _fetch_rows(
    config: SmartsheetConfig,
    cursor: str | None,
    auth: Dict[str, Any],
) -> Tuple[List[Dict[str, Any]], str | None]:
    if config.fixture_path:
        payload = load_fixture(config.fixture_path)
        return [
            {**doc, "source": "smartsheet", "metadata": {**doc.get("metadata", {}), "cursor": cursor}}
            for doc in payload.get("documents", [])
        ], None
    if not config.api_base_url:
        raise ValueError("smartsheet api_base_url is required when fixture_path is not set")
    headers = build_auth_headers(auth)
    headers.setdefault("Accept", "application/json")
    params: Dict[str, Any] = {}
    if config.page_size:
        params["pageSize"] = config.page_size
    if cursor:
        params["page"] = cursor
    url = build_url(config.api_base_url, f"/sheets/{config.sheet_id}", params)
    data = get_json(url, headers=headers)
    rows = data.get("rows", [])
    documents: List[Dict[str, Any]] = []
    for index, row in enumerate(rows):
        row_id = row.get("id") or row.get("rowNumber") or f"{config.sheet_id}:{index}"
        title = f"Smartsheet row {row_id}"
        documents.append(
            {
                "doc_id": str(row_id),
                "title": title,
                "content": json.dumps(row.get("cells") or row, ensure_ascii=True),
                "source": "smartsheet",
                "metadata": {"sheet_id": config.sheet_id, "cursor": cursor},
            }
        )
    next_cursor = None
    if data.get("pageNumber") and data.get("totalPages"):
        page_number = int(data["pageNumber"])
        total_pages = int(data["totalPages"])
        if page_number < total_pages:
            next_cursor = str(page_number + 1)
    return documents, next_cursor
