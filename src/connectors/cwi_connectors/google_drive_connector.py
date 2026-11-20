from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, Iterable, List, Tuple

from .fixture_loader import load_fixture
from .framework import Connector, ConnectorContext, ConnectorState, ConnectorRunResult
from .auth import build_auth_headers, get_json
from .http_client import build_url
from .normalization import normalize_document
from .url_validation import validate_external_url
from .versioning import CONNECTOR_SDK_VERSION


@dataclass(frozen=True)
class GoogleDriveConfig:
    root_folder_id: str
    page_size: int = 50
    fixture_path: str | None = None
    api_base_url: str | None = None


class GoogleDriveConnector(Connector):
    name = "google_drive"
    sdk_version = CONNECTOR_SDK_VERSION

    def __init__(self, config: GoogleDriveConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "GoogleDriveConnector":
        root_folder_id = str(config.get("root_folder_id") or "root")
        page_size = int(config.get("page_size") or 50)
        fixture_path = config.get("fixture_path")
        api_base_url = config.get("api_base_url")
        if api_base_url:
            api_base_url = validate_external_url(str(api_base_url), "google_drive.api_base_url")
        return cls(
            GoogleDriveConfig(
                root_folder_id=root_folder_id,
                page_size=page_size,
                fixture_path=fixture_path,
                api_base_url=api_base_url,
            ),
            auth=auth,
        )

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        items, next_cursor = _fetch_documents(self.config, state.cursor if state else None, self.auth)
        normalized = [normalize_document(item) for item in items]
        return ConnectorRunResult(items=[doc.__dict__ for doc in normalized], next_cursor=next_cursor)


def _fetch_documents(
    config: GoogleDriveConfig,
    cursor: str | None,
    auth: Dict[str, Any],
) -> Tuple[List[Dict[str, Any]], str | None]:
    if config.fixture_path:
        payload = load_fixture(config.fixture_path)
        return [
            {**doc, "source": "google-drive", "metadata": {**doc.get("metadata", {}), "cursor": cursor}}
            for doc in payload.get("documents", [])
        ], None
    if not config.api_base_url:
        raise ValueError("google_drive api_base_url is required when fixture_path is not set")
    headers = build_auth_headers(auth)
    headers.setdefault("Accept", "application/json")
    params: Dict[str, Any] = {"pageSize": config.page_size}
    if cursor:
        params["pageToken"] = cursor
    if config.root_folder_id:
        params["q"] = f"'{config.root_folder_id}' in parents"
    data = get_json(build_url(config.api_base_url, "/files", params), headers=headers)
    documents: List[Dict[str, Any]] = []
    for entry in data.get("files", []):
        doc_id = str(entry.get("id") or "")
        title = str(entry.get("name") or entry.get("id") or "")
        if not doc_id or not title:
            continue
        documents.append(
            {
                "doc_id": doc_id,
                "title": title,
                "content": entry.get("description") or "",
                "source": "google-drive",
                "mime_type": entry.get("mimeType") or "text/plain",
                "metadata": {**entry, "cursor": cursor},
            }
        )
    return documents, data.get("nextPageToken")
