from __future__ import annotations

from node_agent_runtime.config import DegradationConfig, ResilienceConfig
from node_agent_runtime.degradation import (
    SystemMetrics,
    emit_degradation_event,
    evaluate_degradation,
    send_or_queue_degradation_event,
)
from node_agent_runtime.offline_queue import OutboundQueue


def test_simulation_offline_backpressure_queue(tmp_path):
    metrics = SystemMetrics(
        observed_utc="2026-02-01T00:00:00Z",
        load_1=3.0,
        cpu_percent=None,
        disk_free_ratio=0.5,
        memory_free_ratio=None,
    )
    snapshot = evaluate_degradation(metrics, DegradationConfig())
    event = emit_degradation_event(metrics, snapshot, node_id="node-01", state_dir=tmp_path)

    def fail_send(_event):
        raise TimeoutError("partition")

    result = send_or_queue_degradation_event(
        event,
        degradation=DegradationConfig(transport_url="https://coord/events"),
        resilience=ResilienceConfig(max_entries=2, max_bytes=1024, max_attempts=1, backoff_seconds=0),
        state_dir=tmp_path,
        send_fn=fail_send,
    )

    assert result["sent"] is False
    assert result["queued"] is True
    queue = OutboundQueue(tmp_path / "offline_queue", ResilienceConfig(max_entries=2, max_bytes=1024, max_attempts=1))
    assert queue.queue_path.exists()
