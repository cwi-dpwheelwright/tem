from cwi_connectors.delivery_queue import DeliveryQueue


def test_delivery_queue_retries_and_drains(tmp_path):
    queue = DeliveryQueue(str(tmp_path / "queue.json"), max_attempts=2, base_delay_seconds=0)
    queue.enqueue("provenance", {"value": 1}, idempotency_key="key-1")
    calls = {"count": 0}

    def handler(item):
        calls["count"] += 1
        if calls["count"] < 2:
            raise RuntimeError("fail")

    processed = queue.drain("provenance", handler)
    assert processed == 0
    processed = queue.drain("provenance", handler)
    assert processed == 1
    assert queue.depth() == 0
