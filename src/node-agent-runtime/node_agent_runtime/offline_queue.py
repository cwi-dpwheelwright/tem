"""Offline queue with bounded persistence and retry."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable
import json
import uuid

from .config import ResilienceConfig


@dataclass(frozen=True)
class QueueItem:
    item_id: str
    queued_utc: str
    attempts: int
    payload: dict[str, Any]


@dataclass(frozen=True)
class FlushResult:
    sent: int
    failed: int
    remaining: int
    dropped: int


class OutboundQueue:
    def __init__(self, queue_dir: Path, config: ResilienceConfig) -> None:
        self._queue_dir = queue_dir
        self._config = config
        self._queue_path = queue_dir / "queue.jsonl"

    @property
    def queue_path(self) -> Path:
        return self._queue_path

    def enqueue(self, payload: dict[str, Any]) -> QueueItem:
        item = QueueItem(
            item_id=uuid.uuid4().hex,
            queued_utc=_utc_now(),
            attempts=0,
            payload=payload,
        )
        items = self._load_items()
        items.append(item)
        items = _apply_limits(items, self._config)
        self._write_items(items)
        return item

    def flush(
        self,
        send_fn: Callable[[dict[str, Any]], bool],
        *,
        sleep_fn=lambda _: None,
    ) -> FlushResult:
        items = self._load_items()
        remaining: list[QueueItem] = []
        sent = failed = dropped = 0

        for item in items:
            attempts = item.attempts
            delivered = False
            while attempts < self._config.max_attempts:
                try:
                    send_fn(item.payload)
                    delivered = True
                    sent += 1
                    break
                except Exception:
                    attempts += 1
                    if attempts < self._config.max_attempts:
                        sleep_fn(self._config.backoff_seconds * (2 ** (attempts - 1)))
            if delivered:
                continue
            failed += 1
            if attempts >= self._config.max_attempts:
                dropped += 1
                continue
            remaining.append(
                QueueItem(
                    item_id=item.item_id,
                    queued_utc=item.queued_utc,
                    attempts=attempts,
                    payload=item.payload,
                )
            )

        remaining = _apply_limits(remaining, self._config)
        self._write_items(remaining)
        return FlushResult(
            sent=sent,
            failed=failed,
            remaining=len(remaining),
            dropped=dropped,
        )

    def _load_items(self) -> list[QueueItem]:
        if not self._queue_path.exists():
            return []
        items: list[QueueItem] = []
        for line in self._queue_path.read_text(encoding="utf-8").splitlines():
            raw = json.loads(line)
            if not isinstance(raw, dict):
                continue
            payload = raw.get("payload")
            if not isinstance(payload, dict):
                continue
            items.append(
                QueueItem(
                    item_id=str(raw.get("item_id")),
                    queued_utc=str(raw.get("queued_utc")),
                    attempts=int(raw.get("attempts", 0)),
                    payload=payload,
                )
            )
        return items

    def _write_items(self, items: list[QueueItem]) -> None:
        self._queue_dir.mkdir(parents=True, exist_ok=True)
        with self._queue_path.open("w", encoding="utf-8") as handle:
            for item in items:
                payload = {
                    "item_id": item.item_id,
                    "queued_utc": item.queued_utc,
                    "attempts": item.attempts,
                    "payload": item.payload,
                }
                handle.write(json.dumps(payload, sort_keys=True))
                handle.write("\n")


def resolve_queue_dir(state_dir: Path | None, config: ResilienceConfig) -> Path | None:
    if config.queue_dir:
        return config.queue_dir
    if state_dir:
        return state_dir / "offline_queue"
    return None


def _apply_limits(items: list[QueueItem], config: ResilienceConfig) -> list[QueueItem]:
    if not items:
        return items
    items = items[-config.max_entries :]
    total_bytes = 0
    trimmed: list[QueueItem] = []
    for item in reversed(items):
        size = len(json.dumps(item.payload, sort_keys=True))
        if total_bytes + size > config.max_bytes:
            continue
        total_bytes += size
        trimmed.append(item)
    return list(reversed(trimmed))


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
