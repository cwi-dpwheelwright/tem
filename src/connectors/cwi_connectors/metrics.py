from __future__ import annotations

from dataclasses import dataclass, field
from threading import Lock
from typing import Iterable

from .reconciliation import ReconciliationResult


@dataclass
class MetricsRegistry:
    lock: Lock = field(default_factory=Lock)
    connector_runs_total: int = 0
    connector_items_total: int = 0
    connector_failures_total: int = 0
    connector_reconcile_total: int = 0
    connector_reconcile_failures_total: int = 0
    connector_reconcile_tickets_total: int = 0
    connector_queue_depth: int = 0

    def record_run(self, items: int, ok: bool) -> None:
        with self.lock:
            self.connector_runs_total += 1
            self.connector_items_total += items
            if not ok:
                self.connector_failures_total += 1

    def record_reconciliation(self, results: Iterable[ReconciliationResult]) -> None:
        with self.lock:
            for result in results:
                self.connector_reconcile_total += 1
                if not result.ok:
                    self.connector_reconcile_failures_total += 1
                if result.ticket_required:
                    self.connector_reconcile_tickets_total += 1

    def record_queue_depth(self, depth: int) -> None:
        with self.lock:
            self.connector_queue_depth = depth

    def render_prometheus(self) -> str:
        with self.lock:
            return (
                "# HELP cwi_connectors_runs_total Total connector runs\n"
                "# TYPE cwi_connectors_runs_total counter\n"
                f"cwi_connectors_runs_total {self.connector_runs_total}\n"
                "# HELP cwi_connectors_items_total Total items processed\n"
                "# TYPE cwi_connectors_items_total counter\n"
                f"cwi_connectors_items_total {self.connector_items_total}\n"
                "# HELP cwi_connectors_failures_total Total failed runs\n"
                "# TYPE cwi_connectors_failures_total counter\n"
                f"cwi_connectors_failures_total {self.connector_failures_total}\n"
                "# HELP cwi_connectors_reconcile_total Total reconciliation submits\n"
                "# TYPE cwi_connectors_reconcile_total counter\n"
                f"cwi_connectors_reconcile_total {self.connector_reconcile_total}\n"
                "# HELP cwi_connectors_reconcile_failures_total Total reconciliation failures\n"
                "# TYPE cwi_connectors_reconcile_failures_total counter\n"
                f"cwi_connectors_reconcile_failures_total {self.connector_reconcile_failures_total}\n"
                "# HELP cwi_connectors_reconcile_tickets_total Total reconciliation ticket-required responses\n"
                "# TYPE cwi_connectors_reconcile_tickets_total counter\n"
                f"cwi_connectors_reconcile_tickets_total {self.connector_reconcile_tickets_total}\n"
                "# HELP cwi_connectors_queue_depth Current queued delivery items\n"
                "# TYPE cwi_connectors_queue_depth gauge\n"
                f"cwi_connectors_queue_depth {self.connector_queue_depth}\n"
            )
