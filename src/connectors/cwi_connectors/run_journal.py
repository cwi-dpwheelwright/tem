from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List


@dataclass
class RunEntry:
    connector_id: str
    started_utc: str
    finished_utc: str
    items: int
    ok: bool
    error: str | None = None
    queue_depth: int | None = None


class RunJournal:
    def __init__(self, path: str) -> None:
        self.path = Path(path)

    def record(self, entry: RunEntry) -> None:
        entries = self.read()
        entries[entry.connector_id] = entry
        self._save(entries)

    def read(self) -> Dict[str, RunEntry]:
        if not self.path.exists():
            return {}
        raw = json.loads(self.path.read_text(encoding="utf-8"))
        entries: Dict[str, RunEntry] = {}
        for key, value in raw.items():
            entries[key] = RunEntry(
                connector_id=key,
                started_utc=value.get("started_utc", ""),
                finished_utc=value.get("finished_utc", ""),
                items=int(value.get("items", 0)),
                ok=bool(value.get("ok", False)),
                error=value.get("error"),
                queue_depth=value.get("queue_depth"),
            )
        return entries

    def _save(self, entries: Dict[str, RunEntry]) -> None:
        payload = {
            key: {
                "started_utc": entry.started_utc,
                "finished_utc": entry.finished_utc,
                "items": entry.items,
                "ok": entry.ok,
                "error": entry.error,
                "queue_depth": entry.queue_depth,
            }
            for key, entry in entries.items()
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    def snapshot(self) -> List[dict]:
        entries = self.read()
        return [entry.__dict__ for entry in entries.values()]
