from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from typing import Any, Dict, Iterable, List, Tuple

from cwi_etl.conflicts import Conflict, detect_conflicts
from cwi_etl.ir import IRAttribute, IRDocument, IREntity


@dataclass(frozen=True)
class ConflictResolutionPolicy:
    source_priority: Dict[str, int] = field(default_factory=dict)
    high_risk_attributes: List[str] = field(default_factory=list)
    confidence_threshold: float = 0.7


@dataclass(frozen=True)
class ApprovalTicket:
    ticket_id: str
    entity_id: str
    attribute_name: str
    severity: str
    reason: str
    sources: List[str]


@dataclass(frozen=True)
class ReconciliationResult:
    entities: List[IREntity]
    conflicts: List[Conflict]
    approvals: List[ApprovalTicket]


def reconcile_documents(
    documents: Iterable[IRDocument],
    policy: ConflictResolutionPolicy | None = None,
) -> ReconciliationResult:
    policy = policy or ConflictResolutionPolicy()
    conflicts = detect_conflicts(documents, critical_attributes=policy.high_risk_attributes)
    conflict_index = {(c.entity_id, c.attribute_name): c for c in conflicts}

    entity_map: Dict[str, List[IREntity]] = {}
    for document in documents:
        for entity in document.entities:
            entity_map.setdefault(entity.entity_id, []).append(entity)

    reconciled_entities: List[IREntity] = []
    approvals: List[ApprovalTicket] = []

    for entity_id, entities in entity_map.items():
        attribute_map: Dict[str, Dict[str, Any]] = {}
        for entity in entities:
            for attr in entity.attributes:
                attribute_map.setdefault(attr.name, {})[entity.source_id] = attr.value

        resolved_attributes: List[IRAttribute] = []
        for attr_name, values in attribute_map.items():
            resolved_value, sources, confidence = _resolve_value(values, policy)
            resolved_attributes.append(
                IRAttribute(
                    name=attr_name,
                    value=resolved_value,
                    value_type=_infer_type(resolved_value),
                    source_id="reconciled",
                    confidence=confidence,
                    metadata={"sources": sources},
                )
            )

            conflict = conflict_index.get((entity_id, attr_name))
            if conflict and _needs_approval(conflict, confidence, policy):
                approvals.append(
                    ApprovalTicket(
                        ticket_id=_ticket_id(entity_id, attr_name, conflict.conflict_id),
                        entity_id=entity_id,
                        attribute_name=attr_name,
                        severity=conflict.severity,
                        reason=conflict.rationale,
                        sources=sources,
                    )
                )

        reconciled_entities.append(
            IREntity(
                entity_id=entity_id,
                entity_type=entities[0].entity_type,
                attributes=resolved_attributes,
                source_id="reconciled",
                provenance=sorted({src for entity in entities for src in entity.provenance}),
            )
        )

    return ReconciliationResult(
        entities=reconciled_entities,
        conflicts=conflicts,
        approvals=approvals,
    )


def _resolve_value(values: Dict[str, Any], policy: ConflictResolutionPolicy) -> Tuple[Any, List[str], float]:
    if not values:
        return None, [], 0.0

    priority = policy.source_priority
    best_source = max(values.keys(), key=lambda key: priority.get(key, 0))
    if priority.get(best_source, 0) > 0:
        selected_value = values[best_source]
        sources = [best_source]
    else:
        counts: Dict[str, int] = {}
        for value in values.values():
            counts[repr(value)] = counts.get(repr(value), 0) + 1
        best_repr = sorted(counts.items(), key=lambda item: (-item[1], item[0]))[0][0]
        selected_value = next(value for value in values.values() if repr(value) == best_repr)
        sources = [source for source, value in values.items() if repr(value) == best_repr]

    confidence = len(sources) / max(1, len(values))
    return selected_value, sources, confidence


def _needs_approval(conflict: Conflict, confidence: float, policy: ConflictResolutionPolicy) -> bool:
    if conflict.severity == "high" and confidence < policy.confidence_threshold:
        return True
    if conflict.attribute_name.lower() in {name.lower() for name in policy.high_risk_attributes}:
        return confidence < 1.0
    return False


def _ticket_id(entity_id: str, attribute_name: str, conflict_id: str) -> str:
    payload = f"{entity_id}:{attribute_name}:{conflict_id}"
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _infer_type(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "bool"
    if isinstance(value, int) or isinstance(value, float):
        return "number"
    if isinstance(value, list):
        return "list"
    if isinstance(value, dict):
        return "object"
    return "string"
