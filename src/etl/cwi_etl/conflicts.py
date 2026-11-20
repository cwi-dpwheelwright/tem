from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, List, Tuple

from cwi_etl.ir import IRDocument


CRITICAL_ATTRIBUTES = {"id", "status", "amount", "total", "balance"}


@dataclass(frozen=True)
class Conflict:
    conflict_id: str
    entity_id: str
    attribute_name: str
    values_by_source: Dict[str, Any]
    category: str
    severity: str
    rationale: str


def detect_conflicts(
    documents: Iterable[IRDocument],
    critical_attributes: Iterable[str] | None = None,
) -> List[Conflict]:
    critical = set(critical_attributes or CRITICAL_ATTRIBUTES)
    grouped: Dict[Tuple[str, str], Dict[str, Any]] = {}

    for document in documents:
        for entity in document.entities:
            for attr in entity.attributes:
                key = (entity.entity_id, attr.name)
                grouped.setdefault(key, {})[entity.source_id] = attr.value

    conflicts: List[Conflict] = []
    for (entity_id, attr_name), values in grouped.items():
        distinct = _distinct_values(values)
        if len(distinct) <= 1:
            continue
        category = _category_for(values)
        severity = _severity_for(attr_name, category, critical)
        conflict_id = _hash_id(entity_id, attr_name, values)
        conflicts.append(
            Conflict(
                conflict_id=conflict_id,
                entity_id=entity_id,
                attribute_name=attr_name,
                values_by_source=values,
                category=category,
                severity=severity,
                rationale=_build_rationale(values, category),
            )
        )

    return conflicts


def _distinct_values(values: Dict[str, Any]) -> List[str]:
    return sorted({repr(value) for value in values.values()})


def _category_for(values: Dict[str, Any]) -> str:
    if any(value is None or value == "" for value in values.values()):
        return "missing_value"
    types = {type(value) for value in values.values()}
    if len(types) > 1:
        return "type_mismatch"
    return "value_mismatch"


def _severity_for(attribute_name: str, category: str, critical: set[str]) -> str:
    if category == "missing_value":
        return "low"
    if category == "type_mismatch":
        return "medium"
    return "high" if attribute_name.lower() in critical else "medium"


def _hash_id(entity_id: str, attribute_name: str, values: Dict[str, Any]) -> str:
    payload = f"{entity_id}:{attribute_name}:{sorted(values.items())}"
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _build_rationale(values: Dict[str, Any], category: str) -> str:
    if category == "missing_value":
        return "At least one source reported a missing value."
    if category == "type_mismatch":
        return "Sources reported different value types."
    return "Sources reported differing values."
