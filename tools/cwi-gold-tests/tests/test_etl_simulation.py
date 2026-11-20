import asyncio
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "src" / "etl"))

from cwi_etl.ingest_runner import IngestItem, IngestRunner, RetryPolicy


def test_etl_simulation_offline_recovery():
    attempts = {"count": 0}

    async def send(_: IngestItem) -> bool:
        attempts["count"] += 1
        return attempts["count"] > 2

    runner = IngestRunner(retry_policy=RetryPolicy(max_attempts=3, base_delay_seconds=0.0))
    result = asyncio.run(runner.run([IngestItem(doc_id="doc-1", payload={})], send))

    assert result[0][1] is True
    assert attempts["count"] == 3


def test_etl_simulation_partition_failure():
    async def send(_: IngestItem) -> bool:
        return False

    runner = IngestRunner(retry_policy=RetryPolicy(max_attempts=2, base_delay_seconds=0.0))
    result = asyncio.run(runner.run([IngestItem(doc_id="doc-1", payload={})], send))

    assert result[0][1] is False


def test_etl_simulation_backpressure():
    inflight = {"count": 0}

    async def send(_: IngestItem) -> bool:
        inflight["count"] += 1
        try:
            await asyncio.sleep(0.01)
            return inflight["count"] <= 1
        finally:
            inflight["count"] -= 1

    runner = IngestRunner(max_concurrency=2, retry_policy=RetryPolicy(max_attempts=3, base_delay_seconds=0.0))
    items = [IngestItem(doc_id="doc-1", payload={}), IngestItem(doc_id="doc-2", payload={})]
    result = asyncio.run(runner.run(items, send))

    assert all(ok for _, ok in result)
