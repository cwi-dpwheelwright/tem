import json

from cwi_etl.identity import build_content_identity, normalize_content


def test_content_identity_stable() -> None:
    identity_a = build_content_identity("source", "alpha", doc_id="doc-1")
    identity_b = build_content_identity("source", "alpha", doc_id="doc-1")
    assert identity_a.stable_id == identity_b.stable_id
    assert identity_a.content_hash == identity_b.content_hash


def test_content_identity_metadata_ordering() -> None:
    identity_a = build_content_identity("source", "alpha", metadata={"b": 2, "a": 1})
    identity_b = build_content_identity("source", "alpha", metadata={"a": 1, "b": 2})
    assert identity_a.version_hash == identity_b.version_hash


def test_content_identity_changes_on_content() -> None:
    identity_a = build_content_identity("source", "alpha")
    identity_b = build_content_identity("source", "alpha!")
    assert identity_a.content_hash != identity_b.content_hash


def test_normalize_content_trims() -> None:
    assert normalize_content(" alpha \n\n") == "alpha"
