from __future__ import annotations

from contextlib import nullcontext
from dataclasses import dataclass
from time import perf_counter
from typing import Any, Dict, Optional

from .config import EdgeAgentConfig

try:  # pragma: no cover - optional dependency
    from opentelemetry import metrics, trace
    from opentelemetry.exporter.otlp.proto.http.metric_exporter import OTLPMetricExporter
    from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
    from opentelemetry.sdk.metrics import MeterProvider
    from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
    from opentelemetry.sdk.resources import Resource
    from opentelemetry.sdk.trace import TracerProvider
    from opentelemetry.sdk.trace.export import BatchSpanProcessor

    OTEL_AVAILABLE = True
except Exception:  # pragma: no cover - optional dependency
    OTEL_AVAILABLE = False


@dataclass
class SpanTimer:
    start: float

    def duration_ms(self) -> float:
        return max(0.0, (perf_counter() - self.start) * 1000.0)


class Observability:
    def __init__(
        self,
        tracer=None,
        meter=None,
    ) -> None:
        self._tracer = tracer
        self._meter = meter
        self._ingest_counter = meter.create_counter("edge_agent_ingest_total") if meter else None
        self._ingest_latency = (
            meter.create_histogram("edge_agent_ingest_latency_ms") if meter else None
        )
        self._export_counter = meter.create_counter("edge_agent_export_total") if meter else None
        self._export_latency = (
            meter.create_histogram("edge_agent_export_latency_ms") if meter else None
        )
        self._export_delay = (
            meter.create_histogram("edge_agent_export_delay_seconds") if meter else None
        )
        self._buffer_pending = meter.create_up_down_counter("edge_agent_buffer_pending") if meter else None
        self._buffer_bytes = meter.create_up_down_counter("edge_agent_buffer_bytes") if meter else None
        self._degradation_hist = (
            meter.create_histogram("edge_agent_degradation_severity") if meter else None
        )
        self._buffer_pending_value = 0
        self._buffer_bytes_value = 0

    def start_span(self, name: str, attributes: Optional[Dict[str, Any]] = None):
        if not self._tracer:
            return nullcontext()
        return self._tracer.start_as_current_span(name, attributes=attributes)

    def record_ingest(self, ok: bool, status_code: int | None, duration_ms: float) -> None:
        if self._ingest_counter:
            attributes = {"result": "ok" if ok else "error"}
            if status_code is not None:
                attributes["status_code"] = str(status_code)
            self._ingest_counter.add(1, attributes)
        if self._ingest_latency:
            self._ingest_latency.record(duration_ms, {"result": "ok" if ok else "error"})

    def record_buffer(self, pending: int, size_bytes: int) -> None:
        if self._buffer_pending:
            delta = pending - self._buffer_pending_value
            if delta:
                self._buffer_pending.add(delta)
                self._buffer_pending_value = pending
        if self._buffer_bytes:
            delta = size_bytes - self._buffer_bytes_value
            if delta:
                self._buffer_bytes.add(delta)
                self._buffer_bytes_value = size_bytes

    def record_export(self, ok: bool, status_code: int | None, duration_ms: float) -> None:
        if self._export_counter:
            attributes = {"result": "ok" if ok else "error"}
            if status_code is not None:
                attributes["status_code"] = str(status_code)
            self._export_counter.add(1, attributes)
        if self._export_latency:
            self._export_latency.record(duration_ms, {"result": "ok" if ok else "error"})

    def record_export_delay(self, name: str, delay_seconds: float) -> None:
        if self._export_delay:
            self._export_delay.record(delay_seconds, {"queue": name})

    def record_degradation(self, severity: float, resource: float, network: float) -> None:
        if self._degradation_hist:
            self._degradation_hist.record(
                severity,
                {"resource": f"{resource:.3f}", "network": f"{network:.3f}"},
            )


class NoopObservability(Observability):
    def __init__(self) -> None:  # pragma: no cover - trivial
        super().__init__(tracer=None, meter=None)

    def start_span(self, name: str, attributes: Optional[Dict[str, Any]] = None):
        return nullcontext()

    def record_ingest(self, ok: bool, status_code: int | None, duration_ms: float) -> None:
        return

    def record_buffer(self, pending: int, size_bytes: int) -> None:
        return

    def record_export(self, ok: bool, status_code: int | None, duration_ms: float) -> None:
        return

    def record_export_delay(self, name: str, delay_seconds: float) -> None:
        return

    def record_degradation(self, severity: float, resource: float, network: float) -> None:
        return


def init_observability(config: EdgeAgentConfig) -> Observability:
    if not config.otel_enabled:
        return NoopObservability()
    if not OTEL_AVAILABLE:
        print("OpenTelemetry is not installed. Disable otel.enabled or install cwi-edge-agent[otel].")
        return NoopObservability()

    resource = Resource.create({"service.name": config.otel_service_name})

    tracer_provider = TracerProvider(resource=resource)
    span_kwargs: Dict[str, Any] = {}
    if config.otel_otlp_endpoint:
        span_kwargs["endpoint"] = config.otel_otlp_endpoint
    if config.otel_headers:
        span_kwargs["headers"] = config.otel_headers
    exporter = OTLPSpanExporter(**span_kwargs)
    tracer_provider.add_span_processor(BatchSpanProcessor(exporter))
    trace.set_tracer_provider(tracer_provider)
    tracer = trace.get_tracer("edge_agent")

    meter = None
    if config.otel_metrics_enabled:
        metric_kwargs: Dict[str, Any] = {}
        if config.otel_otlp_endpoint:
            metric_kwargs["endpoint"] = config.otel_otlp_endpoint
        if config.otel_headers:
            metric_kwargs["headers"] = config.otel_headers
        metric_exporter = OTLPMetricExporter(**metric_kwargs)
        metric_reader = PeriodicExportingMetricReader(
            metric_exporter,
            export_interval_millis=config.otel_metrics_interval_seconds * 1000,
        )
        metrics.set_meter_provider(MeterProvider(resource=resource, metric_readers=[metric_reader]))
        meter = metrics.get_meter("edge_agent")

    if not config.otel_traces_enabled:
        tracer = None

    return Observability(tracer=tracer, meter=meter)
