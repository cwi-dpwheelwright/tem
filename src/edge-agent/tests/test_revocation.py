from pathlib import Path

import pytest

import edge_agent.revocation as revocation
from edge_agent.revocation import RevocationList, ensure_cert_not_revoked


def test_revocation_check_raises_for_revoked_cert(tmp_path: Path, monkeypatch) -> None:
    revoked = tmp_path / "revoked.txt"
    revoked.write_text("deadbeef\n", encoding="utf-8")

    monkeypatch.setattr(revocation, "load_cert_serial", lambda _: "deadbeef")

    with pytest.raises(RuntimeError, match="revoked"):
        ensure_cert_not_revoked("cert.pem", str(revoked))


def test_revocation_list_loads_serials(tmp_path: Path) -> None:
    revoked = tmp_path / "revoked.txt"
    revoked.write_text("# comment\nabcd1234\nEF01\n", encoding="utf-8")
    entries = RevocationList.load(revoked)
    assert entries.is_revoked("abcd1234")
    assert entries.is_revoked("ef01")


def test_revocation_check_raises_for_crl_hit(monkeypatch) -> None:
    monkeypatch.setattr(revocation, "load_cert_serial", lambda _: "beefcafe")
    monkeypatch.setattr(revocation, "_load_crl_serials", lambda *args, **kwargs: {"beefcafe"})
    with pytest.raises(RuntimeError, match="CRL"):
        ensure_cert_not_revoked("cert.pem", None, crl_url="https://ca.local/crl.pem")
