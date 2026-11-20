from __future__ import annotations

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
class OutlookConfig:
    mailbox: str
    page_size: int = 50
    fixture_path: str | None = None
    api_base_url: str | None = None


class OutlookConnector(Connector):
    name = "outlook"
    sdk_version = CONNECTOR_SDK_VERSION

    def __init__(self, config: OutlookConfig, auth: Dict[str, Any] | None = None) -> None:
        self.config = config
        self.auth = auth or {}

    @classmethod
    def from_config(cls, config: Dict[str, Any], auth: Dict[str, Any]) -> "OutlookConnector":
        mailbox = str(config.get("mailbox") or "inbox")
        page_size = int(config.get("page_size") or 50)
        fixture_path = config.get("fixture_path")
        api_base_url = config.get("api_base_url")
        if api_base_url:
            api_base_url = validate_external_url(str(api_base_url), "outlook.api_base_url")
        return cls(
            OutlookConfig(mailbox=mailbox, page_size=page_size, fixture_path=fixture_path, api_base_url=api_base_url),
            auth=auth,
        )

    def run(self, context: ConnectorContext, state: ConnectorState | None) -> ConnectorRunResult:
        items, next_cursor = _fetch_messages(self.config, state.cursor if state else None, self.auth)
        normalized = [normalize_document(item) for item in items]
        return ConnectorRunResult(items=[doc.__dict__ for doc in normalized], next_cursor=next_cursor)


def _fetch_messages(
    config: OutlookConfig,
    cursor: str | None,
    auth: Dict[str, Any],
) -> Tuple[List[Dict[str, Any]], str | None]:
    if config.fixture_path:
        payload = load_fixture(config.fixture_path)
        return [
            {**doc, "source": "outlook", "metadata": {**doc.get("metadata", {}), "cursor": cursor}}
            for doc in payload.get("documents", [])
        ], None
    if not config.api_base_url:
        raise ValueError("outlook api_base_url is required when fixture_path is not set")
    headers = build_auth_headers(auth)
    headers.setdefault("Accept", "application/json")
    if cursor and cursor.startswith("http"):
        url = cursor
    else:
        mailbox = config.mailbox.strip().lower()
        path = "/me/messages" if not mailbox or mailbox == "me" else f"/users/{config.mailbox}/messages"
        params: Dict[str, Any] = {"$top": config.page_size}
        if cursor:
            params["$skiptoken"] = cursor
        url = build_url(config.api_base_url, path, params)
    data = get_json(url, headers=headers)
    messages: List[Dict[str, Any]] = []
    for entry in data.get("value", []):
        doc_id = str(entry.get("id") or "")
        title = str(entry.get("subject") or entry.get("id") or "")
        if not doc_id or not title:
            continue
        body = entry.get("body") or {}
        content = body.get("content") or entry.get("bodyPreview") or ""
        messages.append(
            {
                "doc_id": doc_id,
                "title": title,
                "content": content,
                "source": "outlook",
                "metadata": {**entry, "cursor": cursor},
            }
        )
    return messages, data.get("@odata.nextLink")
