import json
import os
import uuid
import datetime as dt
import urllib.request


def publish_event(
    event_type: str,
    polarity: str,
    severity: str,
    message: str,
    *,
    data: dict | None = None,
    tenant_id: str | None = None,
    trace_id: str | None = None,
) -> None:
    if os.getenv("EVENT_BUS_ENABLED", "").lower() != "true":
        return

    url = os.getenv("EVENT_BUS_URL")
    if not url:
        return

    payload = {
        "event_id": uuid.uuid4().hex,
        "timestamp_utc": dt.datetime.now(dt.timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z"),
        "service": os.getenv("EVENT_BUS_SERVICE_NAME", "teacher-node"),
        "environment": os.getenv("EVENT_BUS_ENV", "unknown"),
        "type": event_type,
        "polarity": polarity,
        "severity": severity,
        "message": message,
        "tenant_id": tenant_id,
        "trace_id": trace_id,
        "data": data or {},
    }

    headers = {"Content-Type": "application/json"}
    token = os.getenv("EVENT_BUS_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    req = urllib.request.Request(
        url=url,
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=2) as _resp:
            pass
    except Exception:
        return
