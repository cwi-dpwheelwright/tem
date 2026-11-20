from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict


@dataclass(frozen=True)
class ConnectorStatus:
    connector_id: str
    cursor: str | None
    updated_utc: str


class AdminMonitor:
    def __init__(self, state_store_path: str) -> None:
        self.state_store_path = Path(state_store_path)

    def snapshot(self) -> Dict[str, Any]:
        if not self.state_store_path.exists():
            return {"connectors": [], "count": 0}
        raw = json.loads(self.state_store_path.read_text(encoding="utf-8"))
        connectors = [
            ConnectorStatus(
                connector_id=key,
                cursor=value.get("cursor"),
                updated_utc=value.get("updated_utc", ""),
            )
            for key, value in raw.items()
        ]
        return {
            "connectors": [connector.__dict__ for connector in connectors],
            "count": len(connectors),
        }
