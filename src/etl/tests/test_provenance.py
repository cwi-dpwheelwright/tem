from cwi_etl.provenance import build_provenance_event


def test_build_provenance_event_generates_run_id() -> None:
    event = build_provenance_event(
        tenant_id="tenant-1",
        doc_id="doc-1",
        chunk_count=3,
        idempotency_key="idem-1",
        started_utc="2026-01-01T00:00:00Z",
        completed_utc="2026-01-01T00:01:00Z",
    )
    assert event.run_id
    assert event.tenant_id == "tenant-1"
    assert event.doc_id == "doc-1"
    assert event.chunk_count == 3
