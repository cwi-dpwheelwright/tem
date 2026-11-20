from __future__ import annotations

import json
from datetime import datetime, timezone
from typing import Any, Dict


def emit_audit_event(event_type: str, details: Dict[str, Any]) -> None:
    payload = {
        "event_type": event_type,
        "observed_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "details": details,
    }
    print(json.dumps(payload, sort_keys=True))
