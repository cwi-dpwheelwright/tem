from __future__ import annotations

from urllib.parse import urlparse


def validate_http_url(value: str, label: str) -> str:
    cleaned = str(value or "").strip()
    parsed = urlparse(cleaned)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError(f"{label} must be an http(s) URL")
    return cleaned
