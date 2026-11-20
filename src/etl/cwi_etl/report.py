from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, List

from cwi_etl.conflicts import Conflict
from cwi_etl.ir import IREntity, entity_to_dict
from cwi_etl.reconciliation import ApprovalTicket, ReconciliationResult


@dataclass(frozen=True)
class ReconciliationReport:
    run_id: str
    created_utc: str
    entities: List[IREntity]
    conflicts: List[Conflict]
    approvals: List[ApprovalTicket]
    stats: Dict[str, Any]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "run_id": self.run_id,
            "created_utc": self.created_utc,
            "stats": self.stats,
            "entities": [entity_to_dict(entity) for entity in self.entities],
            "conflicts": [conflict_to_dict(conflict) for conflict in self.conflicts],
            "approvals": [approval_to_dict(ticket) for ticket in self.approvals],
        }


def build_report(result: ReconciliationResult, run_id: str) -> ReconciliationReport:
    created_utc = datetime.now(tz=timezone.utc).isoformat()
    stats = {
        "entity_count": len(result.entities),
        "conflict_count": len(result.conflicts),
        "approval_count": len(result.approvals),
    }
    return ReconciliationReport(
        run_id=run_id,
        created_utc=created_utc,
        entities=result.entities,
        conflicts=result.conflicts,
        approvals=result.approvals,
        stats=stats,
    )


def write_report(report: ReconciliationReport, path: str) -> None:
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(report.to_dict(), handle, indent=2, sort_keys=True)


def write_entities(entities: List[IREntity], path: str) -> None:
    payload = [entity_to_dict(entity) for entity in entities]
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)


def conflict_to_dict(conflict: Conflict) -> Dict[str, Any]:
    return {
        "conflict_id": conflict.conflict_id,
        "entity_id": conflict.entity_id,
        "attribute_name": conflict.attribute_name,
        "values_by_source": conflict.values_by_source,
        "category": conflict.category,
        "severity": conflict.severity,
        "rationale": conflict.rationale,
    }


def approval_to_dict(ticket: ApprovalTicket) -> Dict[str, Any]:
    return {
        "ticket_id": ticket.ticket_id,
        "entity_id": ticket.entity_id,
        "attribute_name": ticket.attribute_name,
        "severity": ticket.severity,
        "reason": ticket.reason,
        "sources": ticket.sources,
    }
