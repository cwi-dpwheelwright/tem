from __future__ import annotations

import hashlib
import json
import uuid
from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, List

from cwi_etl.ir import IRAttribute, IRDocument, IREvent


class ConsentMissingError(ValueError):
    pass


class ConsentNotGrantedError(ValueError):
    pass


class SignalValidationError(ValueError):
    pass


class PerformanceValidationError(ValueError):
    pass


SIGNAL_TYPE_ALIASES = {
    "corrections": "correction",
    "correction": "correction",
    "delay": "delay",
    "delays": "delay",
    "confirmation": "confirmation",
    "confirmations": "confirmation",
    "confirm": "confirmation",
    "override": "override",
    "overrides": "override",
}

SIGNAL_UNIT_MAP = {
    "correction": "count",
    "delay": "count",
    "confirmation": "count",
    "override": "count",
}

PERFORMANCE_METRIC_ALIASES = {
    "latency_ms": "response_latency_ms",
    "response_latency_ms": "response_latency_ms",
    "response_latency": "response_latency_ms",
    "response_time_ms": "response_latency_ms",
}

PERFORMANCE_UNIT_MAP = {
    "response_latency_ms": "ms",
}


@dataclass(frozen=True)
class ConsentRecord:
    granted: bool
    granted_utc: str
    source: str | None = None


@dataclass(frozen=True)
class OperatorSignal:
    signal_type: str
    value: float
    unit: str
    occurred_utc: str
    confidence: float = 1.0


@dataclass(frozen=True)
class OperatorPerformanceMetric:
    metric: str
    value: float
    unit: str
    window_utc: str
    confidence: float = 1.0


@dataclass(frozen=True)
class OperatorMemorySignalBatch:
    tenant_id: str
    operator_id: str
    observed_utc: str
    consent: ConsentRecord
    signals: List[OperatorSignal]
    performance: List[OperatorPerformanceMetric]
    session_id: str | None = None
    source_id: str = "operator-memory"

    def to_ir_document(self, doc_id: str | None = None) -> IRDocument:
        document_id = doc_id or f"operator-memory:{self.tenant_id}:{self.operator_id}:{self.observed_utc}"
        consent_source = self.consent.source or self.source_id
        attributes = [
            IRAttribute(
                name="consent_granted",
                value=self.consent.granted,
                value_type="boolean",
                source_id=consent_source,
            ),
            IRAttribute(
                name="consent_granted_utc",
                value=self.consent.granted_utc,
                value_type="date-time",
                source_id=consent_source,
            ),
            IRAttribute(
                name="observed_utc",
                value=self.observed_utc,
                value_type="date-time",
                source_id=self.source_id,
            ),
        ]
        if self.session_id:
            attributes.append(
                IRAttribute(
                    name="session_id",
                    value=self.session_id,
                    value_type="string",
                    source_id=self.source_id,
                )
            )

        events: List[IREvent] = []
        for signal in self.signals:
            payload = {
                "signal_type": signal.signal_type,
                "value": signal.value,
                "unit": signal.unit,
                "confidence": signal.confidence,
            }
            events.append(
                IREvent(
                    event_id=_event_id("signal", payload, signal.occurred_utc),
                    event_type="operator_signal",
                    timestamp_utc=signal.occurred_utc,
                    entity_id=self.operator_id,
                    payload=payload,
                    source_id=self.source_id,
                )
            )

        for metric in self.performance:
            payload = {
                "metric": metric.metric,
                "value": metric.value,
                "unit": metric.unit,
                "confidence": metric.confidence,
            }
            events.append(
                IREvent(
                    event_id=_event_id("performance", payload, metric.window_utc),
                    event_type="operator_performance",
                    timestamp_utc=metric.window_utc,
                    entity_id=self.operator_id,
                    payload=payload,
                    source_id=self.source_id,
                )
            )

        metadata = {
            "tenant_id": self.tenant_id,
            "operator_id": self.operator_id,
            "observed_utc": self.observed_utc,
        }
        return IRDocument(
            doc_id=document_id,
            source_id=self.source_id,
            events=events,
            attributes=attributes,
            metadata=metadata,
        )


@dataclass(frozen=True)
class CorrelationWeights:
    correction: float = 0.5
    delay: float = 0.3
    latency: float = 0.2


@dataclass(frozen=True)
class CorrelationConfig:
    latency_baseline_ms: float = 800.0
    weights: CorrelationWeights = field(default_factory=CorrelationWeights)


@dataclass(frozen=True)
class OperatorMemoryCorrelation:
    correlation_id: str
    tenant_id: str
    operator_id: str
    observed_utc: str
    consent: ConsentRecord
    signals: List[OperatorSignal]
    performance: List[OperatorPerformanceMetric]
    correlation_score: float
    session_id: str | None = None
    notes: str | None = None
    metadata: Dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "correlation_id": self.correlation_id,
            "tenant_id": self.tenant_id,
            "operator_id": self.operator_id,
            "observed_utc": self.observed_utc,
            "consent": consent_to_dict(self.consent),
            "signals": [signal_to_dict(signal) for signal in self.signals],
            "performance": [performance_to_dict(metric) for metric in self.performance],
            "correlation_score": self.correlation_score,
        }
        if self.session_id:
            payload["session_id"] = self.session_id
        if self.notes:
            payload["notes"] = self.notes
        if self.metadata:
            payload["metadata"] = self.metadata
        return payload


def ensure_consent(consent: ConsentRecord | None) -> None:
    if consent is None:
        raise ConsentMissingError("consent record is required")
    if not consent.granted:
        raise ConsentNotGrantedError("consent not granted")
    if not consent.granted_utc:
        raise ConsentMissingError("consent granted_utc is required")


def normalize_signal(signal: OperatorSignal, *, strict: bool = True) -> OperatorSignal:
    if not signal.signal_type:
        raise SignalValidationError("signal_type is required")
    if not signal.occurred_utc:
        raise SignalValidationError("occurred_utc is required")
    normalized_type = _normalize_text(signal.signal_type)
    canonical_type = SIGNAL_TYPE_ALIASES.get(normalized_type, normalized_type)
    if strict and canonical_type not in SIGNAL_UNIT_MAP:
        raise SignalValidationError(f"unsupported signal_type: {signal.signal_type}")
    unit = signal.unit.strip() if signal.unit else SIGNAL_UNIT_MAP.get(canonical_type, "count")
    value = float(signal.value)
    if value < 0:
        raise SignalValidationError("signal value must be non-negative")
    confidence = _clamp(float(signal.confidence))
    return OperatorSignal(
        signal_type=canonical_type,
        value=value,
        unit=unit,
        occurred_utc=signal.occurred_utc,
        confidence=confidence,
    )


def normalize_performance_metric(metric: OperatorPerformanceMetric, *, strict: bool = True) -> OperatorPerformanceMetric:
    if not metric.metric:
        raise PerformanceValidationError("metric is required")
    if not metric.window_utc:
        raise PerformanceValidationError("window_utc is required")
    normalized_metric = _normalize_text(metric.metric)
    canonical_metric = PERFORMANCE_METRIC_ALIASES.get(normalized_metric, normalized_metric)
    if strict and canonical_metric not in PERFORMANCE_UNIT_MAP:
        raise PerformanceValidationError(f"unsupported performance metric: {metric.metric}")
    unit = metric.unit.strip() if metric.unit else PERFORMANCE_UNIT_MAP.get(canonical_metric, "unit")
    value = float(metric.value)
    if value < 0:
        raise PerformanceValidationError("metric value must be non-negative")
    confidence = _clamp(float(metric.confidence))
    return OperatorPerformanceMetric(
        metric=canonical_metric,
        value=value,
        unit=unit,
        window_utc=metric.window_utc,
        confidence=confidence,
    )


def build_operator_memory_batch(
    tenant_id: str,
    operator_id: str,
    observed_utc: str,
    consent: ConsentRecord,
    signals: Iterable[OperatorSignal],
    performance: Iterable[OperatorPerformanceMetric],
    *,
    session_id: str | None = None,
    source_id: str = "operator-memory",
    enforce_consent: bool = True,
    strict: bool = True,
) -> OperatorMemorySignalBatch:
    if not tenant_id or not operator_id or not observed_utc:
        raise ValueError("tenant_id, operator_id, and observed_utc are required")
    if enforce_consent:
        ensure_consent(consent)

    normalized_signals = [normalize_signal(signal, strict=strict) for signal in signals]
    normalized_performance = [normalize_performance_metric(metric, strict=strict) for metric in performance]

    return OperatorMemorySignalBatch(
        tenant_id=tenant_id,
        operator_id=operator_id,
        observed_utc=observed_utc,
        consent=consent,
        signals=normalized_signals,
        performance=normalized_performance,
        session_id=session_id,
        source_id=source_id,
    )


def compute_correlation_score(
    signals: Iterable[OperatorSignal],
    performance: Iterable[OperatorPerformanceMetric],
    config: CorrelationConfig | None = None,
) -> float:
    resolved_config = config or CorrelationConfig()
    signal_list = list(signals)
    performance_list = list(performance)

    corrections = _signal_weight_sum(signal_list, "correction")
    delays = _signal_weight_sum(signal_list, "delay")
    total_signals = _signal_weight_sum(signal_list, None)
    correction_rate = corrections / total_signals if total_signals > 0 else 0.0
    delay_rate = delays / total_signals if total_signals > 0 else 0.0

    avg_latency = _average_latency(performance_list)
    if avg_latency is None or resolved_config.latency_baseline_ms <= 0:
        latency_factor = 0.0
    else:
        latency_factor = _clamp(avg_latency / resolved_config.latency_baseline_ms)

    penalty = (
        correction_rate * resolved_config.weights.correction
        + delay_rate * resolved_config.weights.delay
        + latency_factor * resolved_config.weights.latency
    )
    return round(_clamp(1.0 - penalty), 4)


def build_operator_memory_correlation(
    batch: OperatorMemorySignalBatch,
    *,
    correlation_id: str | None = None,
    config: CorrelationConfig | None = None,
    notes: str | None = None,
    metadata: Dict[str, Any] | None = None,
) -> OperatorMemoryCorrelation:
    ensure_consent(batch.consent)
    score = compute_correlation_score(batch.signals, batch.performance, config)
    resolved_metadata = dict(metadata or {})
    resolved_metadata.setdefault("source_id", batch.source_id)
    return OperatorMemoryCorrelation(
        correlation_id=correlation_id or _default_correlation_id(),
        tenant_id=batch.tenant_id,
        operator_id=batch.operator_id,
        observed_utc=batch.observed_utc,
        consent=batch.consent,
        signals=batch.signals,
        performance=batch.performance,
        correlation_score=score,
        session_id=batch.session_id,
        notes=notes,
        metadata=resolved_metadata,
    )


def write_operator_memory_correlation(correlation: OperatorMemoryCorrelation, path: str) -> None:
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(correlation.to_dict(), handle, indent=2, sort_keys=True)


def consent_to_dict(consent: ConsentRecord) -> Dict[str, Any]:
    payload: Dict[str, Any] = {
        "granted": consent.granted,
        "granted_utc": consent.granted_utc,
    }
    if consent.source:
        payload["source"] = consent.source
    return payload


def signal_to_dict(signal: OperatorSignal) -> Dict[str, Any]:
    return {
        "signal_type": signal.signal_type,
        "value": signal.value,
        "unit": signal.unit,
        "confidence": signal.confidence,
        "occurred_utc": signal.occurred_utc,
    }


def performance_to_dict(metric: OperatorPerformanceMetric) -> Dict[str, Any]:
    return {
        "metric": metric.metric,
        "value": metric.value,
        "unit": metric.unit,
        "confidence": metric.confidence,
        "window_utc": metric.window_utc,
    }


def _default_correlation_id() -> str:
    return f"omc-{uuid.uuid4()}"


def _normalize_text(value: str) -> str:
    return value.strip().lower()


def _clamp(value: float, minimum: float = 0.0, maximum: float = 1.0) -> float:
    if value < minimum:
        return minimum
    if value > maximum:
        return maximum
    return value


def _signal_weight_sum(signals: Iterable[OperatorSignal], signal_type: str | None) -> float:
    total = 0.0
    for signal in signals:
        if signal_type is not None and signal.signal_type != signal_type:
            continue
        weight = signal.value if signal.unit == "count" else 1.0
        if weight < 0:
            continue
        total += weight
    return total


def _average_latency(performance: Iterable[OperatorPerformanceMetric]) -> float | None:
    latency_values: List[float] = []
    for metric in performance:
        if metric.metric != "response_latency_ms":
            continue
        latency_values.append(metric.value)
    if not latency_values:
        return None
    return sum(latency_values) / len(latency_values)


def _event_id(kind: str, payload: Dict[str, Any], observed_utc: str) -> str:
    identity = {"kind": kind, "payload": payload, "observed_utc": observed_utc}
    return f"{kind}:{_stable_hash(identity)}"


def _stable_hash(payload: Dict[str, Any]) -> str:
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(serialized.encode("utf-8")).hexdigest()
