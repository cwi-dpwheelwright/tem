from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from typing import Any, Dict


def _sha256(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def normalize_content(content: str) -> str:
    return "\n".join(line.rstrip() for line in content.replace("\r\n", "\n").replace("\r", "\n").split("\n")).strip()


def _stable_json(data: Dict[str, Any]) -> str:
    return json.dumps(data, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


@dataclass(frozen=True)
class ContentIdentity:
    doc_id: str
    source_id: str
    content_hash: str
    stable_id: str
    version_hash: str


def build_content_identity(
    source_id: str,
    content: str,
    doc_id: str | None = None,
    metadata: Dict[str, Any] | None = None,
) -> ContentIdentity:
    if not source_id:
        raise ValueError("source_id is required")

    normalized = normalize_content(content)
    content_hash = _sha256(normalized)
    doc_identity = doc_id or content_hash
    stable_id = _sha256(f"{source_id}:{doc_identity}")

    metadata_payload = _stable_json(metadata or {})
    version_hash = _sha256(f"{stable_id}:{content_hash}:{metadata_payload}")

    return ContentIdentity(
        doc_id=doc_identity,
        source_id=source_id,
        content_hash=content_hash,
        stable_id=stable_id,
        version_hash=version_hash,
    )
