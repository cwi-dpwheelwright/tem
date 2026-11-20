from __future__ import annotations

import asyncio
import random
import time
from dataclasses import dataclass, field
from typing import Awaitable, Callable, Dict, Iterable, List, Optional, Tuple


@dataclass(frozen=True)
class IngestItem:
    doc_id: str
    payload: Dict[str, object]


@dataclass(frozen=True)
class RetryPolicy:
    max_attempts: int = 3
    base_delay_seconds: float = 1.0
    max_delay_seconds: float = 10.0
    jitter: float = 0.1

    def next_delay(self, attempt: int) -> float:
        raw = min(self.base_delay_seconds * (2 ** (attempt - 1)), self.max_delay_seconds)
        jitter = raw * self.jitter
        return max(0.0, raw + random.uniform(-jitter, jitter))


@dataclass
class MetricsRecorder:
    started: int = 0
    succeeded: int = 0
    failed: int = 0
    total_duration_seconds: float = 0.0

    def record_start(self) -> None:
        self.started += 1

    def record_success(self, duration_seconds: float) -> None:
        self.succeeded += 1
        self.total_duration_seconds += duration_seconds

    def record_failure(self, duration_seconds: float) -> None:
        self.failed += 1
        self.total_duration_seconds += duration_seconds


class Tracer:
    def start_span(self, name: str) -> "TraceSpan":
        return TraceSpan(name)


class TraceSpan:
    def __init__(self, name: str) -> None:
        self.name = name
        self.start_time = time.time()

    def end(self) -> None:
        _ = time.time() - self.start_time


class IngestRunner:
    def __init__(
        self,
        max_concurrency: int = 4,
        retry_policy: RetryPolicy | None = None,
        metrics: MetricsRecorder | None = None,
        tracer: Tracer | None = None,
    ) -> None:
        self._max_concurrency = max_concurrency
        self.semaphore: asyncio.Semaphore | None = None
        self.retry_policy = retry_policy or RetryPolicy()
        self.metrics = metrics or MetricsRecorder()
        self.tracer = tracer or Tracer()

    async def run(
        self,
        items: Iterable[IngestItem],
        send_func: Callable[[IngestItem], Awaitable[bool]],
    ) -> List[Tuple[IngestItem, bool]]:
        if self.semaphore is None:
            self.semaphore = asyncio.Semaphore(self._max_concurrency)
        tasks = [self._run_item(item, send_func) for item in items]
        return await asyncio.gather(*tasks)

    async def _run_item(
        self,
        item: IngestItem,
        send_func: Callable[[IngestItem], Awaitable[bool]],
    ) -> Tuple[IngestItem, bool]:
        if self.semaphore is None:
            self.semaphore = asyncio.Semaphore(self._max_concurrency)
        async with self.semaphore:
            span = self.tracer.start_span("etl.ingest")
            self.metrics.record_start()
            start = time.time()
            attempt = 0
            while True:
                attempt += 1
                success = await send_func(item)
                if success:
                    self.metrics.record_success(time.time() - start)
                    span.end()
                    return item, True
                if attempt >= self.retry_policy.max_attempts:
                    self.metrics.record_failure(time.time() - start)
                    span.end()
                    return item, False
                await asyncio.sleep(self.retry_policy.next_delay(attempt))
