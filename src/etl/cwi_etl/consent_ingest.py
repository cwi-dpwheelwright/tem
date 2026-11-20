from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, Iterable, Optional

from cwi_etl.consent_store import ConsentStore
from cwi_etl.operator_memory import ConsentMissingError, ConsentRecord


class ConsentMappingError(ValueError):
    pass


@dataclass(frozen=True)
class ConsentIngestRecord:
    tenant_id: str
    operator_id: str
    consent: ConsentRecord
    created_utc: str


@dataclass(frozen=True)
class ConsentIngestItem:
    doc_id: str
    payload: Dict[str, Any]
    source_type: str
    source_id: str | None = None
    created_utc: str | None = None


@dataclass
class ConsentIngestMetrics:
    received: int = 0
    mapped: int = 0
    stored: int = 0
    failed: int = 0
    errors: list[str] = field(default_factory=list)

    def record_error(self, message: str) -> None:
        self.failed += 1
        self.errors.append(message)


ConsentMapper = Callable[[Dict[str, Any], Optional[str], Optional[str]], ConsentIngestRecord]


def map_operator_memory_ingest(
    payload: Dict[str, Any],
    source_id: str | None,
    created_utc: str | None,
) -> ConsentIngestRecord:
    tenant_id = _require(payload, "tenant_id")
    operator_id = _require(payload, "operator_id")
    consent_payload = payload.get("consent")
    if not isinstance(consent_payload, dict):
        raise ConsentMappingError("consent payload is required for operator memory ingest")
    granted = consent_payload.get("granted")
    granted_utc = consent_payload.get("granted_utc")
    if granted is None or granted_utc is None:
        raise ConsentMappingError("consent.granted and consent.granted_utc are required")
    consent_source = consent_payload.get("source") or source_id or "operator-memory"
    consent = ConsentRecord(granted=bool(granted), granted_utc=granted_utc, source=consent_source)
    resolved_created_utc = created_utc or granted_utc
    if not resolved_created_utc:
        raise ConsentMappingError("created_utc is required when consent.granted_utc is missing")
    return ConsentIngestRecord(
        tenant_id=tenant_id,
        operator_id=operator_id,
        consent=consent,
        created_utc=resolved_created_utc,
    )


CONSENT_MAPPERS: Dict[str, ConsentMapper] = {
    "operator_memory_ingest": map_operator_memory_ingest,
}


class ConsentIngestJob:
    def __init__(
        self,
        store: ConsentStore,
        logger: logging.Logger | None = None,
    ) -> None:
        self._store = store
        self._logger = logger or logging.getLogger(__name__)

    def run(self, items: Iterable[ConsentIngestItem]) -> ConsentIngestMetrics:
        metrics = ConsentIngestMetrics()
        for item in items:
            metrics.received += 1
            try:
                record = self._map_item(item)
                metrics.mapped += 1
                self._store.record_consent(
                    tenant_id=record.tenant_id,
                    operator_id=record.operator_id,
                    granted=record.consent.granted,
                    granted_utc=record.consent.granted_utc,
                    source=record.consent.source,
                    created_utc=record.created_utc,
                )
                metrics.stored += 1
            except (ConsentMappingError, ConsentMissingError, KeyError, ValueError) as exc:
                metrics.record_error(f"{item.doc_id}: {exc}")
        self._emit_metrics(metrics)
        return metrics

    def _map_item(self, item: ConsentIngestItem) -> ConsentIngestRecord:
        mapper = CONSENT_MAPPERS.get(item.source_type)
        if mapper is None:
            raise ConsentMappingError(f"unsupported consent source_type: {item.source_type}")
        return mapper(item.payload, item.source_id, item.created_utc)

    def _emit_metrics(self, metrics: ConsentIngestMetrics) -> None:
        self._logger.info(
            "consent_ingest received=%s mapped=%s stored=%s failed=%s",
            metrics.received,
            metrics.mapped,
            metrics.stored,
            metrics.failed,
        )
        if metrics.errors:
            for error in metrics.errors:
                self._logger.warning("consent_ingest error=%s", error)


def _require(payload: Dict[str, Any], key: str) -> str:
    value = payload.get(key)
    if value is None or value == "":
        raise ConsentMappingError(f"{key} is required")
    return str(value)
