from __future__ import annotations

from node_agent_runtime.reroute import evaluate_reroute_hint
from node_agent_runtime.config import RerouteConfig


def test_reroute_activation_and_clear(tmp_path):
    config = RerouteConfig(
        warning_threshold=0.7,
        critical_threshold=0.9,
        clear_threshold=0.4,
        cooldown_seconds=0,
    )

    hint = evaluate_reroute_hint(node_id="node-1", severity=0.5, config=config, state_dir=tmp_path)
    assert hint is None

    hint = evaluate_reroute_hint(node_id="node-1", severity=0.95, config=config, state_dir=tmp_path)
    assert hint is not None
    assert hint.action == "activate"

    hint = evaluate_reroute_hint(node_id="node-1", severity=0.6, config=config, state_dir=tmp_path)
    assert hint is None

    hint = evaluate_reroute_hint(node_id="node-1", severity=0.2, config=config, state_dir=tmp_path)
    assert hint is not None
    assert hint.action == "clear"
