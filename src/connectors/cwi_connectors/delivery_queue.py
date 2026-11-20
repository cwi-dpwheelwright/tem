from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List


@dataclass
class QueueItem:
    item_id: str
    kind: str
    payload: Dict[str, Any]
    idempotency_key: str
    attempts: int
    next_attempt_utc: str
    last_error: str | None = None


class DeliveryQueue:
    def __init__(
        self,
        path: str,
        max_attempts: int = 6,
        base_delay_seconds: float = 1.0,
    ) -> None:
        self.path = Path(path)
        self.max_attempts = max_attempts
        self.base_delay_seconds = base_delay_seconds

    def enqueue(self, kind: str, payload: Dict[str, Any], idempotency_key: str, error: str | None = None) -> None:
        items = self._load()
        for item in items:
            if item.kind == kind and item.idempotency_key == idempotency_key:
                return
        item_id = f"{kind}:{idempotency_key}"
        items.append(
            QueueItem(
                item_id=item_id,
                kind=kind,
                payload=payload,
                idempotency_key=idempotency_key,
                attempts=0,
                next_attempt_utc=self._utc_now(),
                last_error=error,
            )
        )
        self._save(items)

    def drain(self, kind: str, handler: Callable[[QueueItem], None], max_items: int = 50) -> int:
        items = self._load()
        now = self._now()
        processed = 0
        remaining: List[QueueItem] = []
        for item in items:
            if item.kind != kind:
                remaining.append(item)
                continue
            if processed >= max_items:
                remaining.append(item)
                continue
            if self._parse_datetime(item.next_attempt_utc) > now:
                remaining.append(item)
                continue
            try:
                handler(item)
                processed += 1
            except Exception as exc:  # pragma: no cover - guarded by tests
                item.attempts += 1
                item.last_error = str(exc)
                delay = self.base_delay_seconds * (2 ** max(0, item.attempts - 1))
                item.next_attempt_utc = (now + timedelta(seconds=delay)).isoformat().replace("+00:00", "Z")
                remaining.append(item)
        self._save(remaining)
        return processed

    def depth(self, kind: str | None = None) -> int:
        items = self._load()
        if not kind:
            return len(items)
        return sum(1 for item in items if item.kind == kind)

    def _load(self) -> List[QueueItem]:
        if not self.path.exists():
            return []
        data = json.loads(self.path.read_text(encoding="utf-8"))
        items = []
        for raw in data:
            items.append(
                QueueItem(
                    item_id=raw.get("item_id", ""),
                    kind=raw.get("kind", ""),
                    payload=raw.get("payload", {}),
                    idempotency_key=raw.get("idempotency_key", ""),
                    attempts=int(raw.get("attempts", 0)),
                    next_attempt_utc=raw.get("next_attempt_utc", self._utc_now()),
                    last_error=raw.get("last_error"),
                )
            )
        return items

    def _save(self, items: List[QueueItem]) -> None:
        payload = [
            {
                "item_id": item.item_id,
                "kind": item.kind,
                "payload": item.payload,
                "idempotency_key": item.idempotency_key,
                "attempts": item.attempts,
                "next_attempt_utc": item.next_attempt_utc,
                "last_error": item.last_error,
            }
            for item in items
        ]
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    @staticmethod
    def _utc_now() -> str:
        return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    @staticmethod
    def _now() -> datetime:
        return datetime.now(timezone.utc)

    @staticmethod
    def _parse_datetime(value: str) -> datetime:
        try:
            return datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError:
            return datetime.now(timezone.utc)
