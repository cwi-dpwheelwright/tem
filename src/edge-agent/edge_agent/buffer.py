from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, List


@dataclass(frozen=True)
class BufferItem:
    item_id: str
    payload: Dict[str, Any]


@dataclass(frozen=True)
class BufferStats:
    pending_count: int
    total_count: int
    size_bytes: int


class JsonlBuffer:
    def __init__(
        self,
        buffer_path: str | Path,
        max_items: int | None = None,
        max_bytes: int | None = None,
        drop_policy: str = "drop_oldest",
    ):
        self.buffer_path = Path(buffer_path)
        self.buffer_path.parent.mkdir(parents=True, exist_ok=True)
        self.cursor_path = self.buffer_path.with_suffix(".cursor")
        self.max_items = max_items
        self.max_bytes = max_bytes
        self.drop_policy = drop_policy
        if not self.buffer_path.exists():
            self.buffer_path.touch()

    def enqueue(self, item: BufferItem) -> bool:
        record = {"id": item.item_id, "payload": item.payload}
        line = json.dumps(record) + "\n"
        if self.drop_policy == "drop_newest" and self._would_exceed_limits(line):
            return False
        with self.buffer_path.open("a", encoding="utf-8") as handle:
            handle.write(line)
        if self.drop_policy == "drop_oldest":
            self._enforce_limits()
        return True

    def pending(self, limit: int = 100) -> List[BufferItem]:
        start = self._read_cursor()
        items: List[BufferItem] = []
        with self.buffer_path.open("r", encoding="utf-8") as handle:
            for idx, line in enumerate(handle):
                if idx < start:
                    continue
                if not line.strip():
                    continue
                record = json.loads(line)
                items.append(BufferItem(item_id=record["id"], payload=record["payload"]))
                if len(items) >= limit:
                    break
        return items

    def stats(self) -> BufferStats:
        total = self._total_count()
        pending = max(0, total - self._read_cursor())
        size_bytes = self.buffer_path.stat().st_size if self.buffer_path.exists() else 0
        return BufferStats(pending_count=pending, total_count=total, size_bytes=size_bytes)

    def ack(self, count: int) -> None:
        if count <= 0:
            return
        current = self._read_cursor()
        self._write_cursor(current + count)
        if self.max_bytes or self.max_items:
            self._maybe_compact()

    def _read_cursor(self) -> int:
        if not self.cursor_path.exists():
            return 0
        content = self.cursor_path.read_text(encoding="utf-8").strip()
        return int(content) if content else 0

    def _write_cursor(self, value: int) -> None:
        self.cursor_path.write_text(str(value), encoding="utf-8")

    def _would_exceed_limits(self, line: str) -> bool:
        pending = self._pending_count()
        if self.max_items is not None and (pending + 1) > self.max_items:
            return True
        if self.max_bytes is not None:
            size = self.buffer_path.stat().st_size
            if (size + len(line.encode("utf-8"))) > self.max_bytes:
                return True
        return False

    def _pending_count(self) -> int:
        return max(0, self._total_count() - self._read_cursor())

    def _total_count(self) -> int:
        count = 0
        with self.buffer_path.open("r", encoding="utf-8") as handle:
            for line in handle:
                if line.strip():
                    count += 1
        return count

    def _enforce_limits(self) -> None:
        if self.max_items is None and self.max_bytes is None:
            return
        pending_lines = self._pending_lines()
        keep: List[str] = []
        size = 0
        for line in reversed(pending_lines):
            line_size = len(line.encode("utf-8"))
            if self.max_items is not None and len(keep) >= self.max_items:
                break
            if self.max_bytes is not None and (size + line_size) > self.max_bytes:
                break
            keep.append(line)
            size += line_size
        keep.reverse()
        self._rewrite_buffer(keep)

    def _maybe_compact(self) -> None:
        if self._read_cursor() <= 0:
            return
        if self.max_bytes is not None and self.buffer_path.stat().st_size > self.max_bytes:
            self._enforce_limits()

    def _pending_lines(self) -> List[str]:
        start = self._read_cursor()
        lines: List[str] = []
        with self.buffer_path.open("r", encoding="utf-8") as handle:
            for idx, line in enumerate(handle):
                if idx < start:
                    continue
                if line.strip():
                    lines.append(line)
        return lines

    def _rewrite_buffer(self, lines: List[str]) -> None:
        with self.buffer_path.open("w", encoding="utf-8") as handle:
            for line in lines:
                handle.write(line)
        self._write_cursor(0)
