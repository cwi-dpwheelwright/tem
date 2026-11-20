import importlib.util
from pathlib import Path

import pytest

from edge_agent.revocation import ensure_cert_not_revoked


def test_mtls_revoked_serial_blocks_client_cert() -> None:
    if not importlib.util.find_spec("cryptography"):
        pytest.skip("cryptography not installed")

    fixture_dir = Path(__file__).parent / "fixtures" / "mtls"
    cert_path = fixture_dir / "client.crt"
    revoked_path = fixture_dir / "revoked_serials.txt"

    with pytest.raises(RuntimeError, match="revoked"):
        ensure_cert_not_revoked(str(cert_path), str(revoked_path))


def test_mtls_non_revoked_serial_allows_client_cert() -> None:
    if not importlib.util.find_spec("cryptography"):
        pytest.skip("cryptography not installed")

    fixture_dir = Path(__file__).parent / "fixtures" / "mtls"
    cert_path = fixture_dir / "client.crt"
    revoked_path = fixture_dir / "revoked_serials_clean.txt"

    ensure_cert_not_revoked(str(cert_path), str(revoked_path))
