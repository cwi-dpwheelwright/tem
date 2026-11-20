from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Dict, List

from .normalization import DocumentRecord
from .vector_ingest import VectorIngestClient


@dataclass
class RetentionEntry:
    doc_id: str
    tenant_id: str
    created_utc: str


class RetentionStore:
    def __init__(self, path: str) -> None:
        self.path = Path(path)

    def load(self) -> Dict[str, RetentionEntry]:
        if not self.path.exists():
            return {}
        data = json.loads(self.path.read_text(encoding="utf-8"))
        entries: Dict[str, RetentionEntry] = {}
        for doc_id, raw in data.items():
            entries[doc_id] = RetentionEntry(
                doc_id=doc_id,
                tenant_id=raw.get("tenant_id", ""),
                created_utc=raw.get("created_utc", ""),
            )
        return entries

    def save(self, entries: Dict[str, RetentionEntry]) -> None:
        payload = {
            doc_id: {"tenant_id": entry.tenant_id, "created_utc": entry.created_utc}
            for doc_id, entry in entries.items()
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


class RetentionEnforcer:
    def __init__(
        self,
        store: RetentionStore,
        vector_client: VectorIngestClient | None,
        retention_days: int | None,
    ) -> None:
        self.store = store
        self.vector_client = vector_client
        self.retention_days = retention_days

    def record(self, documents: List[DocumentRecord], tenant_id: str) -> None:
        entries = self.store.load()
        for doc in documents:
            created_utc = str((doc.metadata or {}).get("created_utc") or (doc.metadata or {}).get("updated_utc") or "")
            if not created_utc:
                created_utc = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
            entries[doc.doc_id] = RetentionEntry(
                doc_id=doc.doc_id,
                tenant_id=tenant_id,
                created_utc=created_utc,
            )
        self.store.save(entries)

    def purge_expired(self) -> List[str]:
        if not self.retention_days:
            return []
        if not self.vector_client:
            return []
        cutoff = datetime.now(timezone.utc) - timedelta(days=self.retention_days)
        entries = self.store.load()
        deleted: List[str] = []
        for doc_id, entry in list(entries.items()):
            created = self._parse_utc(entry.created_utc)
            if created and created < cutoff:
                if self.vector_client:
                    result = self.vector_client.delete_document(
                        doc_id=doc_id,
                        tenant_id=entry.tenant_id or "tenant-1",
                        idempotency_key=f"retention:{doc_id}",
                    )
                    if not result.ok:
                        continue
                deleted.append(doc_id)
                entries.pop(doc_id, None)
        if deleted:
            self.store.save(entries)
        return deleted

    @staticmethod
    def _parse_utc(value: str) -> datetime | None:
        if not value:
            return None
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError:
            return None
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed
