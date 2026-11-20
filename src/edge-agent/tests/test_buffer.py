from pathlib import Path

from edge_agent.buffer import BufferItem, JsonlBuffer


def test_buffer_enqueue_and_ack(tmp_path: Path) -> None:
    buffer_path = tmp_path / "buffer.jsonl"
    buffer = JsonlBuffer(buffer_path)
    assert buffer.enqueue(BufferItem(item_id="item-1", payload={"value": 1}))
    assert buffer.enqueue(BufferItem(item_id="item-2", payload={"value": 2}))

    pending = buffer.pending(limit=10)
    assert [item.item_id for item in pending] == ["item-1", "item-2"]

    buffer.ack(1)
    pending_after = buffer.pending(limit=10)
    assert [item.item_id for item in pending_after] == ["item-2"]


def test_buffer_persists_cursor(tmp_path: Path) -> None:
    buffer_path = tmp_path / "buffer.jsonl"
    buffer = JsonlBuffer(buffer_path)
    assert buffer.enqueue(BufferItem(item_id="item-1", payload={"value": 1}))
    buffer.ack(1)

    buffer_reload = JsonlBuffer(buffer_path)
    assert buffer_reload.pending(limit=10) == []


def test_buffer_drop_newest_policy(tmp_path: Path) -> None:
    buffer_path = tmp_path / "buffer.jsonl"
    buffer = JsonlBuffer(buffer_path, max_items=1, drop_policy="drop_newest")
    assert buffer.enqueue(BufferItem(item_id="item-1", payload={"value": 1}))
    assert buffer.enqueue(BufferItem(item_id="item-2", payload={"value": 2})) is False
    pending = buffer.pending(limit=10)
    assert [item.item_id for item in pending] == ["item-1"]


def test_buffer_drop_oldest_policy(tmp_path: Path) -> None:
    buffer_path = tmp_path / "buffer.jsonl"
    buffer = JsonlBuffer(buffer_path, max_items=2, drop_policy="drop_oldest")
    assert buffer.enqueue(BufferItem(item_id="item-1", payload={"value": 1}))
    assert buffer.enqueue(BufferItem(item_id="item-2", payload={"value": 2}))
    assert buffer.enqueue(BufferItem(item_id="item-3", payload={"value": 3}))
    pending = buffer.pending(limit=10)
    assert [item.item_id for item in pending] == ["item-2", "item-3"]


def test_buffer_stats(tmp_path: Path) -> None:
    buffer_path = tmp_path / "buffer.jsonl"
    buffer = JsonlBuffer(buffer_path)
    buffer.enqueue(BufferItem(item_id="item-1", payload={"value": 1}))
    buffer.enqueue(BufferItem(item_id="item-2", payload={"value": 2}))
    buffer.ack(1)

    stats = buffer.stats()
    assert stats.total_count == 2
    assert stats.pending_count == 1
    assert stats.size_bytes > 0
