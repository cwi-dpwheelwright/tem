from pathlib import Path

from edge_agent.buffer import BufferItem, JsonlBuffer
from edge_agent.config import load_config
from edge_agent.survival import compute_degradation
from edge_agent.backoff import CircuitBreaker


def test_compute_degradation_from_buffer_pressure(tmp_path: Path) -> None:
    config_path = tmp_path / "edge-agent.yaml"
    buffer_path = tmp_path / "buffer.jsonl"
    config_path.write_text(
        "\n".join(
            [
                "gateway_url: \"https://gateway.local\"",
                "tenant_id: \"tenant-1\"",
                "poll_interval_seconds: 5",
                f"buffer_path: \"{buffer_path.as_posix()}\"",
                "buffer_max_items: 10",
                "identity:",
                "  base_url: \"http://identity.local\"",
                "  tenant_id: \"tenant-1\"",
                "  username: \"edge-agent\"",
                "  password: \"secret\"",
            ]
        ),
        encoding="utf-8",
    )

    config, _ = load_config(config_path)
    buffer = JsonlBuffer(buffer_path, max_items=config.buffer_max_items)
    for idx in range(9):
        buffer.enqueue(BufferItem(item_id=f"item-{idx}", payload={"value": idx}))

    snapshot = compute_degradation(buffer, CircuitBreaker(), config)
    assert snapshot.resource_severity >= config.degradation_buffer_critical_ratio
