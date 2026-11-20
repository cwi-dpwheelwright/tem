import pytest

from cwi_etl.consent_ingest import ConsentIngestItem, ConsentIngestJob, map_operator_memory_ingest
from cwi_etl.consent_store import ConsentStore, ConsentStoreConfig


@pytest.mark.integration
def test_map_operator_memory_ingest() -> None:
    payload = {
        "tenant_id": "tenant-demo",
        "operator_id": "op-217",
        "consent": {
            "granted": True,
            "granted_utc": "2026-02-02T17:55:00Z",
            "source": "carplay-ui",
        },
    }

    record = map_operator_memory_ingest(payload, source_id="operator-memory", created_utc="2026-02-02T17:55:10Z")

    assert record.tenant_id == "tenant-demo"
    assert record.operator_id == "op-217"
    assert record.consent.granted is True
    assert record.consent.granted_utc == "2026-02-02T17:55:00Z"
    assert record.consent.source == "carplay-ui"
    assert record.created_utc == "2026-02-02T17:55:10Z"


@pytest.mark.integration
def test_consent_ingest_job_records(tmp_path) -> None:
    db_path = tmp_path / "consent.db"
    store = ConsentStore(ConsentStoreConfig(path=str(db_path)))
    job = ConsentIngestJob(store)

    items = [
        ConsentIngestItem(
            doc_id="item-1",
            source_type="operator_memory_ingest",
            source_id="operator-memory",
            created_utc="2026-02-02T17:55:10Z",
            payload={
                "tenant_id": "tenant-demo",
                "operator_id": "op-217",
                "consent": {
                    "granted": True,
                    "granted_utc": "2026-02-02T17:55:00Z",
                    "source": "carplay-ui",
                },
            },
        )
    ]

    metrics = job.run(items)

    assert metrics.received == 1
    assert metrics.mapped == 1
    assert metrics.stored == 1
    assert metrics.failed == 0
    consent = store.get_latest_consent(tenant_id="tenant-demo", operator_id="op-217")
    assert consent is not None
    assert consent.granted is True
    assert consent.granted_utc == "2026-02-02T17:55:00Z"
    assert consent.source == "carplay-ui"


@pytest.mark.integration
def test_consent_ingest_job_handles_missing_consent(tmp_path) -> None:
    db_path = tmp_path / "consent.db"
    store = ConsentStore(ConsentStoreConfig(path=str(db_path)))
    job = ConsentIngestJob(store)

    items = [
        ConsentIngestItem(
            doc_id="item-2",
            source_type="operator_memory_ingest",
            source_id="operator-memory",
            created_utc="2026-02-02T17:55:10Z",
            payload={
                "tenant_id": "tenant-demo",
                "operator_id": "op-217",
            },
        )
    ]

    metrics = job.run(items)

    assert metrics.received == 1
    assert metrics.mapped == 0
    assert metrics.stored == 0
    assert metrics.failed == 1
    assert store.get_latest_consent(tenant_id="tenant-demo", operator_id="op-217") is None
