from __future__ import annotations

import time
import uuid
from dataclasses import dataclass
from typing import Any, Dict

from .config import RuntimeConfig, load_runtime_config
from .connector_run_pipeline import ConnectorRunPipeline, RunPayload
from .delivery_queue import DeliveryQueue
from .framework import ConnectorContext
from .metrics import MetricsRegistry
from .pipeline import ConnectorPipeline, PipelineConfig
from .provenance import ProvenanceClient
from .reconciliation import ReconciliationClient
from .registry import resolve_connector
from .retention import RetentionEnforcer, RetentionStore
from .run_journal import RunEntry, RunJournal
from .secrets import resolve_secret
from .state_store import FileStateStore, PostgresStateStore, StateStore, utc_now
from .vector_ingest import VectorIngestClient


@dataclass
class RuntimeOptions:
    config_path: str
    health_host: str = "0.0.0.0"
    health_port: int = 8080


def build_state_store(config: RuntimeConfig) -> StateStore:
    if config.state_store.kind == "postgres":
        if not config.state_store.postgres_dsn:
            raise ValueError("state_store.postgres_dsn is required for postgres state store")
        return PostgresStateStore(resolve_secret(config.state_store.postgres_dsn) or "")
    if not config.state_store.path:
        raise ValueError("state_store.path is required for file state store")
    return FileStateStore(config.state_store.path)


class ConnectorRuntime:
    def __init__(self, config: RuntimeConfig, store: StateStore, metrics: MetricsRegistry) -> None:
        self.config = config
        self.store = store
        self.metrics = metrics
        self.queue = DeliveryQueue(config.delivery_queue_path) if config.delivery_queue_path else None
        self.run_journal = RunJournal(config.run_journal_path) if config.run_journal_path else None

        self.vector_client = None
        if config.vector_ingest_url and config.vector_ingest_token:
            self.vector_client = VectorIngestClient(
                config.vector_ingest_url,
                config.vector_ingest_token,
            )

        self.provenance_client = None
        if config.orchestrator_url and config.orchestrator_token:
            self.provenance_client = ProvenanceClient(
                config.orchestrator_url,
                config.orchestrator_token,
                queue=self.queue,
            )

        self.reconciliation_client = None
        if config.reconciliation_url and config.reconciliation_token:
            self.reconciliation_client = ReconciliationClient(
                config.reconciliation_url,
                config.reconciliation_token,
                queue=self.queue,
            )

        self.retention_enforcer = None
        if config.retention_state_path:
            store = RetentionStore(config.retention_state_path)
            self.retention_enforcer = RetentionEnforcer(store, self.vector_client, config.retention_days)

        self.pipeline = ConnectorPipeline(
            PipelineConfig(
                chunk_size=config.chunk_size,
                chunk_overlap=config.chunk_overlap,
                redaction_tokens=config.redaction_tokens or [],
                retention_days=config.retention_days,
            ),
            vector_client=self.vector_client,
            provenance_client=self.provenance_client,
            reconciliation_client=self.reconciliation_client,
            retention_enforcer=self.retention_enforcer,
        )
        self.run_pipeline = ConnectorRunPipeline(self.pipeline)

    def run_once(self, connector_id: str | None = None) -> Dict[str, Any]:
        if self.provenance_client:
            self.provenance_client.flush_queue()
        if self.reconciliation_client:
            self.reconciliation_client.flush_queue()
        results: list[dict[str, Any]] = []
        for connector in self.config.connectors:
            if not connector.enabled:
                continue
            if connector_id and connector.connector_id != connector_id:
                continue
            context = ConnectorContext(
                tenant_id=self.config.tenant_id,
                workspace_id=self.config.workspace_id,
                connector_id=connector.connector_id,
            )
            state = self.store.load(connector.connector_id)
            connector_class = resolve_connector(connector.connector_type)
            connector_instance = connector_class.from_config(connector.config, connector.auth)

            ok = True
            items_count = 0
            error: str | None = None
            started_utc = utc_now()
            try:
                response = self.run_pipeline.run(
                    RunPayload(
                        connector=connector_instance,
                        context=context,
                        state=state,
                        connector_type=connector.connector_type,
                        source_priority=connector.source_priority,
                        run_id=str(uuid.uuid4()),
                    )
                )
                if response.ok and response.data:
                    result = response.data.get("result")
                    pipeline_result = response.data.get("pipeline")
                    if result:
                        self.store.save(connector.connector_id, result.next_cursor)
                    if pipeline_result:
                        items_count = len(pipeline_result.documents)
                        if pipeline_result.reconciliation_results:
                            self.metrics.record_reconciliation(pipeline_result.reconciliation_results)
                else:
                    ok = False
            except Exception as exc:
                ok = False
                error = str(exc)
            finally:
                self.metrics.record_run(items_count, ok)
                queue_depth = self.queue.depth() if self.queue else None
                finished_utc = utc_now()
                if self.run_journal:
                    self.run_journal.record(
                        RunEntry(
                            connector_id=connector.connector_id,
                            started_utc=started_utc,
                            finished_utc=finished_utc,
                            items=items_count,
                            ok=ok,
                            error=error,
                            queue_depth=queue_depth,
                        )
                    )
                results.append(
                    {
                        "connector_id": connector.connector_id,
                        "ok": ok,
                        "items": items_count,
                        "started_utc": started_utc,
                        "finished_utc": finished_utc,
                        "error": error,
                        "queue_depth": queue_depth,
                    }
                )
        if self.queue:
            self.metrics.record_queue_depth(self.queue.depth())
        return {"runs": results, "count": len(results)}

    def run_forever(self) -> None:
        while True:
            self.run_once()
            time.sleep(self.config.poll_interval_seconds)

    def readiness_check(self) -> bool:
        try:
            self.store.load("__ready__")
            return True
        except Exception:
            return False

    def admin_status(self) -> Dict[str, Any]:
        run_entries = self.run_journal.read() if self.run_journal else {}
        connectors = []
        for connector in self.config.connectors:
            state = self.store.load(connector.connector_id)
            run_entry = run_entries.get(connector.connector_id)
            connectors.append(
                {
                    "connector_id": connector.connector_id,
                    "type": connector.connector_type,
                    "enabled": connector.enabled,
                    "cursor": state.cursor if state else None,
                    "updated_utc": state.updated_utc if state else None,
                    "last_run": run_entry.__dict__ if run_entry else None,
                }
            )
        return {
            "connectors": connectors,
            "count": len(connectors),
            "queue_depth": self.queue.depth() if self.queue else 0,
        }

def load_runtime(options: RuntimeOptions) -> ConnectorRuntime:
    config = load_runtime_config(options.config_path)
    store = build_state_store(config)
    metrics = MetricsRegistry()
    return ConnectorRuntime(config=config, store=store, metrics=metrics)
