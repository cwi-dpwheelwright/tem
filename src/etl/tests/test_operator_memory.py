import json
from pathlib import Path

import pytest

from cwi_etl.operator_memory import (
    ConsentNotGrantedError,
    ConsentRecord,
    OperatorPerformanceMetric,
    OperatorSignal,
    build_operator_memory_batch,
    build_operator_memory_correlation,
    compute_correlation_score,
    normalize_signal,
)

FIXTURE_PATH = Path(__file__).resolve().parent / "fixtures" / "operator_memory_correlation.output.json"


def test_consent_required() -> None:
    consent = ConsentRecord(granted=False, granted_utc="2026-02-02T17:55:00Z", source="carplay-ui")
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

    with pytest.raises(ConsentNotGrantedError):
        build_operator_memory_batch(
            tenant_id="tenant-demo",
            operator_id="op-217",
            observed_utc="2026-02-02T18:10:00Z",
            consent=consent,
            signals=signals,
            performance=performance,
        )


def test_signal_normalization() -> None:
    signal = OperatorSignal(
        signal_type="Corrections",
        value=2,
        unit="",
        occurred_utc="2026-02-02T18:00:12Z",
        confidence=1.2,
    )
    normalized = normalize_signal(signal)
    assert normalized.signal_type == "correction"
    assert normalized.unit == "count"
    assert normalized.confidence == 1.0


def test_correlation_score_computation() -> None:
    signals = [
        OperatorSignal(
            signal_type="correction",
            value=1,
            unit="count",
            occurred_utc="2026-02-02T18:00:12Z",
            confidence=0.92,
        ),
        OperatorSignal(
            signal_type="delay",
            value=1,
            unit="count",
            occurred_utc="2026-02-02T18:02:00Z",
            confidence=0.85,
        ),
        OperatorSignal(
            signal_type="confirmation",
            value=1,
            unit="count",
            occurred_utc="2026-02-02T18:03:30Z",
            confidence=0.9,
        ),
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

    score = compute_correlation_score(signals, performance)
    assert score == pytest.approx(0.6283, rel=1e-4)


def test_build_correlation_matches_fixture() -> None:
    payload = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))
    consent_payload = payload["consent"]
    consent = ConsentRecord(
        granted=consent_payload["granted"],
        granted_utc=consent_payload["granted_utc"],
        source=consent_payload.get("source"),
    )
    signals = [
        OperatorSignal(
            signal_type=signal["signal_type"],
            value=signal["value"],
            unit=signal["unit"],
            occurred_utc=signal["occurred_utc"],
            confidence=signal.get("confidence", 1.0),
        )
        for signal in payload["signals"]
    ]
    performance = [
        OperatorPerformanceMetric(
            metric=metric["metric"],
            value=metric["value"],
            unit=metric["unit"],
            window_utc=metric["window_utc"],
            confidence=metric.get("confidence", 1.0),
        )
        for metric in payload["performance"]
    ]

    batch = build_operator_memory_batch(
        tenant_id=payload["tenant_id"],
        operator_id=payload["operator_id"],
        observed_utc=payload["observed_utc"],
        consent=consent,
        signals=signals,
        performance=performance,
        session_id=payload.get("session_id"),
    )
    correlation = build_operator_memory_correlation(
        batch,
        correlation_id=payload["correlation_id"],
        notes=payload.get("notes"),
        metadata=payload.get("metadata"),
    )

    assert correlation.to_dict() == payload
