"""Enrollment request building and submission."""

from __future__ import annotations

import json
import os
import time
from datetime import datetime, timedelta, timezone
from typing import Any
from urllib import request
import ssl
from pathlib import Path
import string

from cryptography import x509
from cryptography.hazmat.primitives import hashes

from .config import AgentConfig, ConfigError, EnrollmentTlsConfig
from .identity import IdentityMaterial
from .local_ask import describe_local_capabilities
from .local_model import describe_local_capability_details, describe_local_model_health


def build_enrollment_payload(
    config: AgentConfig, identity: IdentityMaterial, *, cert_fingerprint: str | None = None
) -> dict[str, Any]:
    resolved_fingerprint = cert_fingerprint or resolve_cert_fingerprint(config)
    capabilities = {capability: True for capability in describe_local_capabilities(config)}
    capabilities.update(describe_local_capability_details(config))
    health = {
        "status": "ENROLLING",
        "runtime_version": config.control_session.runtime_version,
        "policy_profile_version": config.control_session.policy_profile_version,
    }
    health.update(describe_local_model_health(config))
    return {
        "node_id": identity.node_id,
        "tenant_id": config.control_session.tenant_id or "",
        "owner_user_id": config.control_session.owner_user_id or "",
        "device_class": "home-node",
        "capabilities": capabilities,
        "pack_versions": {},
        "policy_profile_version": config.control_session.policy_profile_version,
        "health": health,
        "cert_fingerprint": resolved_fingerprint,
        "protocol_version": config.enrollment.protocol_version,
    }


def submit_enrollment(
    config: AgentConfig, identity: IdentityMaterial, *, timeout_seconds: int
) -> tuple[int, str]:
    cert_path, key_path, cert = select_enrollment_certificate(config.enrollment.tls)
    fingerprint = resolve_cert_fingerprint(config, selected_cert=cert, selected_cert_path=cert_path)
    payload = build_enrollment_payload(config, identity, cert_fingerprint=fingerprint)
    body = json.dumps(payload, indent=2, sort_keys=True).encode("utf-8")
    headers = {
        "Content-Type": "application/json",
        "X-Coordination-Protocol": config.enrollment.protocol_version,
    }
    auth_header = resolve_enrollment_auth_header(config)
    if auth_header:
        headers.update(auth_header)
    req = request.Request(
        config.enrollment_url,
        data=body,
        headers=headers,
        method="POST",
    )
    context = build_tls_context(config.enrollment.tls, cert_path=cert_path, key_path=key_path)
    with request.urlopen(req, timeout=timeout_seconds, context=context) as resp:
        status = resp.status
        response_body = resp.read().decode("utf-8")
    return status, response_body


def resolve_cert_fingerprint(
    config: AgentConfig,
    *,
    selected_cert: x509.Certificate | None = None,
    selected_cert_path: Path | None = None,
) -> str:
    if config.enrollment.cert_fingerprint:
        normalized = _normalize_fingerprint(config.enrollment.cert_fingerprint)
        if selected_cert or selected_cert_path:
            computed = (
                selected_cert.fingerprint(hashes.SHA256()).hex()
                if selected_cert
                else load_certificate_fingerprint(selected_cert_path)
            )
            if normalized != computed:
                raise ConfigError("Enrollment cert fingerprint does not match configured mTLS certificate")
        return normalized
    if selected_cert:
        return selected_cert.fingerprint(hashes.SHA256()).hex()
    if selected_cert_path:
        return load_certificate_fingerprint(selected_cert_path)
    if config.enrollment.tls.cert_path or config.enrollment.tls.cert_paths:
        cert_path, _, cert = select_enrollment_certificate(config.enrollment.tls)
        if cert:
            return cert.fingerprint(hashes.SHA256()).hex()
        if cert_path:
            return load_certificate_fingerprint(cert_path)
    raise ConfigError("Enrollment cert fingerprint missing (set enrollment.cert_fingerprint or enrollment.mtls.cert_path)")


def load_certificate_fingerprint(cert_path: Path) -> str:
    cert = _load_certificate(cert_path)
    return cert.fingerprint(hashes.SHA256()).hex()


def select_enrollment_certificate(
    tls: EnrollmentTlsConfig, *, now: float | None = None
) -> tuple[Path | None, Path | None, x509.Certificate | None]:
    pairs = _build_enrollment_pairs(tls)
    if not pairs:
        return None, None, None
    timestamp = now if now is not None else time.time()
    rotation_seconds = max(0, tls.rotation_seconds)
    grace_seconds = max(0, tls.rotation_grace_seconds)
    start_index = int(timestamp // rotation_seconds) % len(pairs) if rotation_seconds > 0 else 0
    for offset in range(len(pairs)):
        cert_path, key_path = pairs[(start_index + offset) % len(pairs)]
        if cert_path is None or key_path is None:
            continue
        if not cert_path.exists():
            raise ConfigError(f"Enrollment mTLS cert not found: {cert_path}")
        if not key_path.exists():
            raise ConfigError(f"Enrollment mTLS key not found: {key_path}")
        cert = _load_certificate(cert_path)
        if _is_cert_expiring(cert, timestamp, grace_seconds):
            continue
        if _is_cert_revoked(cert, tls, now=timestamp):
            continue
        return cert_path, key_path, cert
    raise ConfigError("No valid enrollment mTLS certificates available")


def _build_enrollment_pairs(tls: EnrollmentTlsConfig) -> list[tuple[Path | None, Path | None]]:
    if tls.cert_paths or tls.key_paths:
        if len(tls.cert_paths) != len(tls.key_paths):
            raise ConfigError("enrollment.mtls.cert_paths and enrollment.mtls.key_paths must be paired")
        return list(zip(tls.cert_paths, tls.key_paths, strict=True))
    if tls.cert_path or tls.key_path:
        if not tls.cert_path or not tls.key_path:
            raise ConfigError("enrollment.mtls.cert_path and enrollment.mtls.key_path must be paired")
        return [(tls.cert_path, tls.key_path)]
    return []


def _is_cert_expiring(cert: x509.Certificate, timestamp: float, grace_seconds: int) -> bool:
    now_dt = datetime.fromtimestamp(timestamp, tz=timezone.utc)
    if hasattr(cert, "not_valid_before_utc"):
        not_before_raw = cert.not_valid_before_utc
    else:
        not_before_raw = cert.not_valid_before
    if hasattr(cert, "not_valid_after_utc"):
        not_after_raw = cert.not_valid_after_utc
    else:
        not_after_raw = cert.not_valid_after
    not_before = _coerce_utc(not_before_raw)
    not_after = _coerce_utc(not_after_raw)
    if now_dt < not_before:
        return True
    if now_dt >= not_after - timedelta(seconds=grace_seconds):
        return True
    return False


def _is_cert_revoked(cert: x509.Certificate, tls: EnrollmentTlsConfig, *, now: float) -> bool:
    serial = cert.serial_number
    if tls.revocation_denylist_path:
        denylist = _load_revocation_denylist(tls.revocation_denylist_path)
        if serial in denylist:
            return True
    crl = _load_revocation_crl(tls, now=now)
    if crl:
        for revoked in crl:
            if revoked.serial_number == serial:
                return True
    return False


def _load_certificate(cert_path: Path) -> x509.Certificate:
    raw = cert_path.read_bytes()
    if b"BEGIN CERTIFICATE" in raw:
        return x509.load_pem_x509_certificate(raw)
    return x509.load_der_x509_certificate(raw)


def _load_revocation_denylist(path: Path) -> set[int]:
    serials: set[int] = set()
    for idx, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        cleaned = line.split("#", 1)[0].split(";", 1)[0].strip()
        if not cleaned:
            continue
        serial = _parse_serial(cleaned)
        if serial is None:
            continue
        serials.add(serial)
    return serials


def _parse_serial(value: str) -> int | None:
    cleaned = value.strip().lower().replace(":", "").replace(" ", "")
    if not cleaned:
        return None
    base = 10
    if cleaned.startswith("0x"):
        cleaned = cleaned[2:]
        base = 16
    elif any(ch in "abcdef" for ch in cleaned):
        base = 16
    if not cleaned:
        return None
    if base == 16:
        if any(ch not in string.hexdigits for ch in cleaned):
            raise ConfigError("Invalid enrollment.mtls.revocation_denylist_path entry")
    else:
        if not cleaned.isdigit():
            raise ConfigError("Invalid enrollment.mtls.revocation_denylist_path entry")
    return int(cleaned, base)


def _load_revocation_crl(tls: EnrollmentTlsConfig, *, now: float) -> x509.CertificateRevocationList | None:
    if not tls.revocation_crl_url and not tls.revocation_crl_path:
        return None
    raw: bytes | None = None
    if tls.revocation_crl_url:
        raw = _fetch_crl_bytes(tls, now=now)
    if raw is None and tls.revocation_crl_path:
        raw = tls.revocation_crl_path.read_bytes()
    if raw is None:
        if tls.revocation_fail_open:
            return None
        raise ConfigError("Enrollment mTLS CRL could not be loaded")
    return _load_crl_from_bytes(raw)


def _fetch_crl_bytes(tls: EnrollmentTlsConfig, *, now: float) -> bytes | None:
    cache_path = tls.revocation_crl_cache_path
    if cache_path and cache_path.exists():
        age = now - cache_path.stat().st_mtime
        if tls.revocation_crl_refresh_seconds <= 0 or age < tls.revocation_crl_refresh_seconds:
            return cache_path.read_bytes()
    try:
        with request.urlopen(tls.revocation_crl_url, timeout=tls.revocation_crl_timeout_seconds) as resp:
            data = resp.read()
    except Exception as exc:
        if cache_path and cache_path.exists():
            return cache_path.read_bytes()
        if tls.revocation_fail_open:
            return None
        raise ConfigError("Failed to fetch enrollment mTLS CRL") from exc
    if cache_path:
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        cache_path.write_bytes(data)
    return data


def _load_crl_from_bytes(raw: bytes) -> x509.CertificateRevocationList:
    if b"BEGIN X509 CRL" in raw or b"BEGIN CRL" in raw:
        return x509.load_pem_x509_crl(raw)
    return x509.load_der_x509_crl(raw)


def _coerce_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc)


def build_tls_context(
    tls: EnrollmentTlsConfig,
    *,
    cert_path: Path | None = None,
    key_path: Path | None = None,
) -> ssl.SSLContext | None:
    if not any((tls.cert_path, tls.key_path, tls.ca_bundle_path, tls.cert_paths, tls.key_paths)):
        return None
    context = ssl.create_default_context(cafile=str(tls.ca_bundle_path) if tls.ca_bundle_path else None)
    context.check_hostname = tls.verify_hostname
    selected_cert_path = cert_path
    selected_key_path = key_path
    if selected_cert_path is None and selected_key_path is None:
        selected_cert_path, selected_key_path, _ = select_enrollment_certificate(tls)
    if selected_cert_path and selected_key_path:
        context.load_cert_chain(certfile=str(selected_cert_path), keyfile=str(selected_key_path))
    return context


def resolve_enrollment_auth_header(config: AgentConfig, *, now: float | None = None) -> dict[str, str] | None:
    auth = config.enrollment.auth
    tokens: list[str] = []
    if auth.token_env:
        value = os.getenv(auth.token_env)
        if value:
            tokens.append(value.strip())
    if not tokens and auth.token:
        tokens.append(auth.token.strip())
    if not tokens and auth.tokens:
        tokens.extend(token.strip() for token in auth.tokens if token.strip())
    if not tokens:
        return None

    token = _select_rotated_token(tokens, auth.token_rotation_seconds, now=now)
    token_type = auth.token_type.strip() if auth.token_type else ""
    if token_type:
        return {"Authorization": f"{token_type} {token}"}
    return {"Authorization": token}


def _normalize_fingerprint(value: str) -> str:
    cleaned = value.strip().lower().replace(":", "")
    if not cleaned:
        raise ConfigError("Invalid enrollment.cert_fingerprint")
    if any(ch not in "0123456789abcdef" for ch in cleaned):
        raise ConfigError("Enrollment cert fingerprint must be hex")
    return cleaned


def _select_rotated_token(tokens: list[str], rotation_seconds: int, *, now: float | None = None) -> str:
    if len(tokens) == 1 or rotation_seconds <= 0:
        return tokens[0]
    timestamp = now if now is not None else time.time()
    index = int(timestamp // rotation_seconds) % len(tokens)
    return tokens[index]
