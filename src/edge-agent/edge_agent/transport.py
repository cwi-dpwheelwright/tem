from __future__ import annotations

import hashlib
import ssl
from typing import Iterable


def build_ssl_context(
    cert_path: str | None,
    key_path: str | None,
    ca_bundle_path: str | None,
    verify_hostname: bool = True,
) -> ssl.SSLContext:
    context = ssl.create_default_context(cafile=ca_bundle_path)
    context.check_hostname = verify_hostname
    context.verify_mode = ssl.CERT_REQUIRED
    if cert_path and key_path:
        context.load_cert_chain(certfile=cert_path, keyfile=key_path)
    return context


def normalize_fingerprints(pins: Iterable[str]) -> set[str]:
    normalized: set[str] = set()
    for value in pins:
        if not value:
            continue
        cleaned = value.strip().lower().replace("sha256:", "").replace(":", "")
        if cleaned:
            normalized.add(cleaned)
    return normalized


def verify_pinned_fingerprint(response, pins: Iterable[str]) -> None:
    normalized = normalize_fingerprints(pins)
    if not normalized:
        return

    cert_bytes = _extract_peer_cert_bytes(response)
    fingerprint = hashlib.sha256(cert_bytes).hexdigest()
    if fingerprint not in normalized:
        raise RuntimeError("TLS pinning check failed: peer fingerprint not in allowlist.")


def _extract_peer_cert_bytes(response) -> bytes:
    try:
        sock = response.fp.raw._sock
    except Exception:
        try:
            sock = response.fp._sock
        except Exception as exc:
            raise RuntimeError("Unable to access peer certificate for pinning.") from exc
    try:
        return sock.getpeercert(binary_form=True)
    except Exception as exc:  # pragma: no cover - depends on socket type
        raise RuntimeError("Unable to read peer certificate for pinning.") from exc
