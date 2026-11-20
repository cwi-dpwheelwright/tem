from __future__ import annotations

import ipaddress
import os
from urllib.parse import urlparse


def _env_flag(name: str) -> bool:
    return os.environ.get(name, "").strip().lower() in {"1", "true", "yes", "on"}


def _is_private_host(host: str) -> bool:
    if not host:
        return True
    lowered = host.lower()
    if lowered in {"localhost"}:
        return True
    if lowered.endswith(".local") or lowered.endswith(".internal"):
        return True
    try:
        ip = ipaddress.ip_address(lowered)
    except ValueError:
        return False
    return (
        ip.is_loopback
        or ip.is_private
        or ip.is_link_local
        or ip.is_multicast
        or ip.is_reserved
        or ip.is_unspecified
    )


def allow_insecure_http() -> bool:
    return _env_flag("CWI_CONNECTORS_ALLOW_INSECURE_HTTP")


def allow_private_urls() -> bool:
    return _env_flag("CWI_CONNECTORS_ALLOW_PRIVATE_URLS")


def validate_http_url(
    value: str,
    label: str,
    *,
    allow_http: bool = True,
    allow_private: bool = True,
) -> str:
    cleaned = str(value or "").strip()
    parsed = urlparse(cleaned)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError(f"{label} must be an http(s) URL")
    if parsed.username or parsed.password:
        raise ValueError(f"{label} must not include embedded credentials")
    if parsed.scheme == "http" and not allow_http:
        raise ValueError(f"{label} must use https")
    host = parsed.hostname or ""
    if not host:
        raise ValueError(f"{label} must include a hostname")
    if not allow_private and _is_private_host(host):
        raise ValueError(f"{label} must not target private or localhost hosts")
    return cleaned


def validate_external_url(value: str, label: str) -> str:
    return validate_http_url(
        value,
        label,
        allow_http=allow_insecure_http(),
        allow_private=allow_private_urls(),
    )
