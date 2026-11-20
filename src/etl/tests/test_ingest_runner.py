import asyncio

from cwi_etl.ingest_runner import IngestItem, IngestRunner, MetricsRecorder, RetryPolicy


def test_ingest_runner_retries_and_succeeds():
    attempts = {"count": 0}

    async def send(item: IngestItem) -> bool:
        attempts["count"] += 1
        return attempts["count"] >= 2

    runner = IngestRunner(retry_policy=RetryPolicy(max_attempts=3, base_delay_seconds=0.0))
    result = asyncio.run(runner.run([IngestItem(doc_id="doc-1", payload={})], send))

    assert result[0][1] is True
    assert attempts["count"] == 2


def test_ingest_runner_fails_after_max_attempts():
    async def send(item: IngestItem) -> bool:
        return False

    runner = IngestRunner(retry_policy=RetryPolicy(max_attempts=2, base_delay_seconds=0.0))
    result = asyncio.run(runner.run([IngestItem(doc_id="doc-1", payload={})], send))

    assert result[0][1] is False


def test_ingest_runner_records_metrics_and_tracing():
    spans = []

    class RecordingSpan:
        def __init__(self, name: str) -> None:
            self.name = name
            self.ended = False

        def end(self) -> None:
            self.ended = True

    class RecordingTracer:
        def start_span(self, name: str) -> RecordingSpan:
            span = RecordingSpan(name)
            spans.append(span)
            return span

    async def send(_: IngestItem) -> bool:
        return True

    metrics = MetricsRecorder()
    tracer = RecordingTracer()
    runner = IngestRunner(
        metrics=metrics,
        tracer=tracer,
        retry_policy=RetryPolicy(max_attempts=1, base_delay_seconds=0.0),
    )
    result = asyncio.run(runner.run([IngestItem(doc_id="doc-1", payload={})], send))

    assert result[0][1] is True
    assert metrics.started == 1
    assert metrics.succeeded == 1
    assert metrics.failed == 0
    assert spans and spans[0].name == "etl.ingest"
    assert spans[0].ended is True
