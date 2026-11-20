import pytest

from cwi_etl.consent_store import ConsentStore, ConsentStoreConfig
from cwi_etl.operator_memory import ConsentMissingError, OperatorPerformanceMetric, OperatorSignal
from cwi_etl.pipeline import ingest_operator_memory_signals


@pytest.mark.integration
def test_ingest_operator_memory_resolves_consent_from_store(tmp_path) -> None:
    db_path = tmp_path / "consent.db"
    store = ConsentStore(ConsentStoreConfig(path=str(db_path)))
    store.record_consent(
        tenant_id="tenant-demo",
        operator_id="op-217",
        granted=True,
        granted_utc="2026-02-02T17:55:00Z",
        source="carplay-ui",
        created_utc="2026-02-02T17:55:10Z",
    )

    signals = [
        OperatorSignal(
            signal_type="correction",
            value=1,
            unit="count",
            occurred_utc="2026-02-02T18:00:12Z",
            confidence=0.92,
        )
    ]
    performance = [
        OperatorPerformanceMetric(
            metric="response_latency_ms",
            value=420,
            unit="ms",
            window_utc="2026-02-02T18:05:00Z",
            confidence=0.88,
        )
    ]

    batch = ingest_operator_memory_signals(
        tenant_id="tenant-demo",
        operator_id="op-217",
        observed_utc="2026-02-02T18:10:00Z",
        consent=None,
        signals=signals,
        performance=performance,
        consent_store=store,
    )

    assert batch.consent.granted is True
    assert batch.consent.granted_utc == "2026-02-02T17:55:00Z"
    assert batch.consent.source == "carplay-ui"


@pytest.mark.integration
def test_ingest_operator_memory_missing_consent_raises(tmp_path) -> None:
    db_path = tmp_path / "consent.db"
    store = ConsentStore(ConsentStoreConfig(path=str(db_path)))

    signals = [
        OperatorSignal(
            signal_type="correction",
            value=1,
            unit="count",
            occurred_utc="2026-02-02T18:00:12Z",
            confidence=0.92,
        )
    ]
    performance = [
        OperatorPerformanceMetric(
            metric="response_latency_ms",
            value=420,
            unit="ms",
            window_utc="2026-02-02T18:05:00Z",
            confidence=0.88,
        )
    ]

    with pytest.raises(ConsentMissingError):
        ingest_operator_memory_signals(
            tenant_id="tenant-demo",
            operator_id="op-217",
            observed_utc="2026-02-02T18:10:00Z",
            consent=None,
            signals=signals,
            performance=performance,
            consent_store=store,
        )
