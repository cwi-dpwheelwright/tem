from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict

from .decorators import retry_decorator, telemetry_decorator
from .execution import ChainHandler, DomainServiceBase, Handler, ServiceResponse
from .framework import Connector, ConnectorContext, ConnectorRunResult
from .pipeline import ConnectorPipeline, PipelineContext
from .state_store import ConnectorState


@dataclass
class RunPayload:
    connector: Connector
    context: ConnectorContext
    state: ConnectorState | None
    connector_type: str
    source_priority: int
    run_id: str


class FetchHandler(Handler):
    def handle(self, payload: Dict[str, Any]) -> ServiceResponse:
        run_payload: RunPayload = payload["run"]
        result = run_payload.connector.run(run_payload.context, run_payload.state)
        return ServiceResponse.success({"result": result})


class ConnectorRunPipeline(DomainServiceBase):
    def __init__(self, pipeline: ConnectorPipeline) -> None:
        self.pipeline = pipeline

    def run(self, run_payload: RunPayload) -> ServiceResponse:
        pipeline_context = PipelineContext(
            tenant_id=run_payload.context.tenant_id,
            workspace_id=run_payload.context.workspace_id,
            connector_id=run_payload.context.connector_id,
            connector_type=run_payload.connector_type,
            source=run_payload.connector.name,
            source_priority=run_payload.source_priority,
            run_id=run_payload.run_id,
        )
        handler = ChainHandler(
            FetchHandler(),
            telemetry_decorator("fetch"),
            retry_decorator(),
            lambda inner: _NormalizeDecorator(inner, self.pipeline, pipeline_context),
            telemetry_decorator("normalize"),
        )
        return self.execute(lambda: handler.handle({"run": run_payload, "pipeline_context": pipeline_context}))


class _NormalizeDecorator:
    def __init__(self, handler: Handler, pipeline: ConnectorPipeline, context: PipelineContext) -> None:
        self.handler = handler
        self.pipeline = pipeline
        self.context = context

    def handle(self, payload: Dict[str, Any]) -> ServiceResponse:
        response = self.handler.handle(payload)
        if not response.ok or not response.data:
            return response
        result = response.data.get("result")
        if not isinstance(result, ConnectorRunResult):
            return response
        pipeline_result = self.pipeline.run(list(result.items), self.context)
        return ServiceResponse.success({"result": result, "pipeline": pipeline_result})
