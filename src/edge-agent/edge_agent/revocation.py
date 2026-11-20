from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Set
from urllib.request import urlopen
from urllib.error import URLError
import time

x509: Any | None
default_backend: Any | None

try:
    from cryptography import x509 as _x509
    from cryptography.hazmat.backends import default_backend as _default_backend
except Exception:  # pragma: no cover - optional dependency
    x509 = None
    default_backend = None
else:
    x509 = _x509
    default_backend = _default_backend


@dataclass(frozen=True)
class RevocationList:
    serials: Set[str]

    @staticmethod
    def load(path: str | Path) -> "RevocationList":
        serials: Set[str] = set()
        for line in Path(path).read_text(encoding="utf-8").splitlines():
            entry = line.strip()
            if not entry or entry.startswith("#"):
                continue
            serials.add(entry.lower())
        return RevocationList(serials=serials)

    def is_revoked(self, serial: str) -> bool:
        return serial.lower() in self.serials


def load_cert_serial(cert_path: str | Path) -> str:
    if x509 is None or default_backend is None:
        raise RuntimeError("cryptography is required for cert parsing. Install with cwi-edge-agent[security].")
    data = Path(cert_path).read_bytes()
    cert = x509.load_pem_x509_certificate(data, default_backend())
    return format(cert.serial_number, "x").lower()


def ensure_cert_not_revoked(
    cert_path: str | None,
    revoked_serials_path: str | None,
    crl_url: str | None = None,
    crl_cache_path: str | None = None,
    crl_refresh_seconds: int | None = None,
) -> None:
    if not cert_path:
        return
    serial = load_cert_serial(cert_path)
    if revoked_serials_path:
        revoked = RevocationList.load(revoked_serials_path)
        if revoked.is_revoked(serial):
            raise RuntimeError("mTLS client certificate is revoked (denylist).")
    if crl_url:
        crl_serials = _load_crl_serials(crl_url, crl_cache_path, crl_refresh_seconds)
        if serial in crl_serials:
            raise RuntimeError("mTLS client certificate is revoked (CRL).")


def _load_crl_serials(
    crl_url: str,
    crl_cache_path: str | None,
    crl_refresh_seconds: int | None,
) -> Set[str]:
    if x509 is None or default_backend is None:
        raise RuntimeError("cryptography is required for CRL parsing. Install with cwi-edge-agent[security].")
    crl_bytes = _fetch_crl_bytes(crl_url, crl_cache_path, crl_refresh_seconds)
    crl = _load_crl(crl_bytes)
    return {format(entry.serial_number, "x").lower() for entry in crl}


def _fetch_crl_bytes(
    crl_url: str,
    crl_cache_path: str | None,
    crl_refresh_seconds: int | None,
) -> bytes:
    cache_path = Path(crl_cache_path) if crl_cache_path else None
    if cache_path and cache_path.exists() and crl_refresh_seconds:
        age = time.time() - cache_path.stat().st_mtime
        if age < crl_refresh_seconds:
            return cache_path.read_bytes()
    try:
        with urlopen(crl_url, timeout=10.0) as response:
            data = response.read()
    except URLError as exc:
        if cache_path and cache_path.exists():
            return cache_path.read_bytes()
        raise RuntimeError(f"Failed to fetch CRL from {crl_url}: {exc}") from exc
    if cache_path:
        cache_path.parent.mkdir(parents=True, exist_ok=True)
        cache_path.write_bytes(data)
    return data


def _load_crl(data: bytes):
    if x509 is None or default_backend is None:
        raise RuntimeError("cryptography is required for CRL parsing. Install with cwi-edge-agent[security].")
    try:
        return x509.load_pem_x509_crl(data, default_backend())
    except ValueError:
        return x509.load_der_x509_crl(data, default_backend())
