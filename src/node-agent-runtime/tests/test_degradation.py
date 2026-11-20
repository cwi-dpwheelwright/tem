from __future__ import annotations

from node_agent_runtime.config import DegradationConfig
import json

from node_agent_runtime.degradation import (
    SystemMetrics,
    emit_degradation_event,
    evaluate_degradation,
    event_payload,
    send_degradation_event,
)


def test_degradation_cpu_severity():
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=3.0,
        cpu_percent=None,
        disk_free_ratio=0.5,
        memory_free_ratio=None,
    )
    config = DegradationConfig(cpu_warning_load=1.0, cpu_critical_load=2.0)
    snapshot = evaluate_degradation(metrics, config)
    assert snapshot.cpu_severity == 1.0
    assert snapshot.severity == 1.0


def test_degradation_disk_severity():
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=None,
        cpu_percent=None,
        disk_free_ratio=0.04,
        memory_free_ratio=None,
    )
    config = DegradationConfig(disk_warning_free_ratio=0.1, disk_critical_free_ratio=0.05)
    snapshot = evaluate_degradation(metrics, config)
    assert snapshot.disk_severity == 0.95
    assert snapshot.severity == 0.95


def test_degradation_none_metrics():
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=None,
        cpu_percent=None,
        disk_free_ratio=None,
        memory_free_ratio=None,
    )
    snapshot = evaluate_degradation(metrics, DegradationConfig())
    assert snapshot.severity == 0.0


def test_degradation_cpu_percent_fallback():
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=None,
        cpu_percent=92.0,
        disk_free_ratio=0.5,
        memory_free_ratio=0.5,
    )
    config = DegradationConfig(cpu_warning_percent=70.0, cpu_critical_percent=90.0)
    snapshot = evaluate_degradation(metrics, config)
    assert snapshot.cpu_severity == 1.0
    assert snapshot.severity == 1.0


def test_degradation_memory_severity():
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=None,
        cpu_percent=None,
        disk_free_ratio=0.5,
        memory_free_ratio=0.05,
    )
    config = DegradationConfig(memory_warning_free_ratio=0.2, memory_critical_free_ratio=0.1)
    snapshot = evaluate_degradation(metrics, config)
    assert snapshot.memory_severity == 0.95
    assert snapshot.severity == 0.95


def test_emit_degradation_event_writes_files(tmp_path):
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=1.2,
        cpu_percent=None,
        disk_free_ratio=0.3,
        memory_free_ratio=None,
    )
    snapshot = evaluate_degradation(metrics, DegradationConfig())
    event = emit_degradation_event(metrics, snapshot, node_id="node-01", state_dir=tmp_path)
    payload = event_payload(event)
    metrics_path = tmp_path / "degradation" / "metrics" / f"{event.event_id}.json"
    assert metrics_path.exists()
    events_path = tmp_path / "degradation" / "events.jsonl"
    assert events_path.exists()
    line = events_path.read_text(encoding="utf-8").strip().splitlines()[0]
    loaded = json.loads(line)
    assert loaded["event_id"] == event.event_id
    assert loaded["node_id"] == "node-01"
    assert payload["provenance"]["metrics_path"] == str(metrics_path)


def test_send_degradation_event_posts(monkeypatch):
    metrics = SystemMetrics(
        observed_utc="2026-01-31T00:00:00Z",
        load_1=1.2,
        cpu_percent=None,
        disk_free_ratio=0.3,
        memory_free_ratio=None,
    )
    snapshot = evaluate_degradation(metrics, DegradationConfig())
    event = emit_degradation_event(metrics, snapshot, node_id="node-01", state_dir=None)

    captured = {}

    class FakeResponse:
        def __init__(self):
            self.status = 200

        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            return False

    def fake_urlopen(req, timeout):
        captured["url"] = req.full_url
        captured["timeout"] = timeout
        captured["body"] = req.data
        return FakeResponse()

    monkeypatch.setattr("node_agent_runtime.degradation.request.urlopen", fake_urlopen)

    config = DegradationConfig(transport_url="https://example.com/events", transport_timeout_seconds=3)
    sent = send_degradation_event(event, config)
    assert sent is True
    assert captured["url"] == "https://example.com/events"
    assert captured["timeout"] == 3
    payload = json.loads(captured["body"].decode("utf-8"))
    assert payload["event_id"] == event.event_id
