from __future__ import annotations

from node_agent_runtime.config import PriorityConfig
from node_agent_runtime.priority import build_priority_headers, extract_priority, normalize_priority


def test_normalize_priority_labels():
    assert normalize_priority("critical").level == 100
    assert normalize_priority("high").level == 80
    assert normalize_priority("normal").level == 50
    assert normalize_priority("low").level == 20


def test_extract_priority_from_payload():
    info = extract_priority({"priority": "high"})
    assert info.label == "high"
    info = extract_priority({"priority_level": 90})
    assert info.label == "high"
    info = extract_priority({"metadata": {"priority": "low"}})
    assert info.level == 20


def test_build_priority_headers():
    config = PriorityConfig(header_name="X-Test-Priority")
    headers = build_priority_headers("critical", config)
    assert headers["X-Test-Priority"] == "critical"
    assert headers["X-Test-Priority-Level"] == "100"
