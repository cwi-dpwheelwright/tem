from __future__ import annotations

import json
import os
from typing import Any, Dict, Mapping
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from .versioning import CONNECTOR_SDK_VERSION


DEFAULT_TIMEOUT_SECONDS = float(os.getenv("CWI_CONNECTORS_HTTP_TIMEOUT_SECONDS", "20"))
DEFAULT_MAX_RESPONSE_BYTES = int(os.getenv("CWI_CONNECTORS_MAX_RESPONSE_BYTES", "10485760"))
DEFAULT_USER_AGENT = os.getenv("CWI_CONNECTORS_USER_AGENT") or f"cwi-connectors/{CONNECTOR_SDK_VERSION}"


def with_user_agent(headers: Mapping[str, str] | None) -> Dict[str, str]:
    merged = dict(headers or {})
    merged.setdefault("User-Agent", DEFAULT_USER_AGENT)
    return merged


def read_limited(response, max_bytes: int = DEFAULT_MAX_RESPONSE_BYTES) -> str:
    chunks: list[bytes] = []
    total = 0
    while True:
        chunk = response.read(65536)
        if not chunk:
            break
        total += len(chunk)
        if total > max_bytes:
            raise RuntimeError("response_too_large")
        chunks.append(chunk)
    return b"".join(chunks).decode("utf-8")


def fetch_json(
    url: str,
    *,
    headers: Mapping[str, str] | None = None,
    timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
    max_bytes: int = DEFAULT_MAX_RESPONSE_BYTES,
) -> Dict[str, Any]:
    request = Request(url, headers=with_user_agent(headers), method="GET")
    with urlopen(request, timeout=timeout_seconds) as response:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
        payload = read_limited(response, max_bytes=max_bytes)
    return json.loads(payload)


def post_json(
    url: str,
    payload: Dict[str, Any],
    *,
    headers: Mapping[str, str] | None = None,
    timeout_seconds: float = DEFAULT_TIMEOUT_SECONDS,
    max_bytes: int = DEFAULT_MAX_RESPONSE_BYTES,
) -> tuple[int, str]:
    request = Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers=with_user_agent(headers),
        method="POST",
    )
    with urlopen(request, timeout=timeout_seconds) as response:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
        body = read_limited(response, max_bytes=max_bytes)
        return response.status, body


def build_url(base_url: str, path: str, params: Dict[str, Any] | None = None) -> str:
    base = base_url.rstrip("/")
    full = f"{base}/{path.lstrip('/')}"
    if params:
        full = f"{full}?{urlencode(params, doseq=True)}"
    return full

