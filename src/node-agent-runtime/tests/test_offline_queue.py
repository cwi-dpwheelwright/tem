from __future__ import annotations

from node_agent_runtime.offline_queue import OutboundQueue, resolve_queue_dir
from node_agent_runtime.config import ResilienceConfig


def test_offline_queue_enqueue_and_flush(tmp_path):
    config = ResilienceConfig(max_entries=10, max_bytes=1024, max_attempts=2, backoff_seconds=0)
    queue = OutboundQueue(tmp_path, config)
    queue.enqueue({"event": "one"})
    queue.enqueue({"event": "two"})

    sent = []

    def sender(payload):
        sent.append(payload)
        return True

    result = queue.flush(sender)
    assert result.sent == 2
    assert result.remaining == 0
    assert sent == [{"event": "one"}, {"event": "two"}]


def test_offline_queue_retry_and_drop(tmp_path):
    config = ResilienceConfig(max_entries=10, max_bytes=1024, max_attempts=2, backoff_seconds=0)
    queue = OutboundQueue(tmp_path, config)
    queue.enqueue({"event": "fail"})

    calls = {"count": 0}

    def sender(_payload):
        calls["count"] += 1
        raise TimeoutError("partition")

    result = queue.flush(sender)
    assert result.failed == 1
    assert result.dropped == 1
    assert result.remaining == 0
    assert calls["count"] == 2


def test_offline_queue_bounds(tmp_path):
    config = ResilienceConfig(max_entries=2, max_bytes=100, max_attempts=1, backoff_seconds=0)
    queue = OutboundQueue(tmp_path, config)
    queue.enqueue({"event": "one"})
    queue.enqueue({"event": "two"})
    queue.enqueue({"event": "three"})

    lines = queue.queue_path.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2


def test_resolve_queue_dir_defaults(tmp_path):
    config = ResilienceConfig()
    resolved = resolve_queue_dir(tmp_path, config)
    assert resolved == tmp_path / "offline_queue"
