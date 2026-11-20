from cwi_connectors.streaming import (
    BoundedBuffer,
    BufferFullError,
    SimulatedStreamConfig,
    SimulatedStreamingAdapter,
    StreamEvent,
    build_stream_documents,
)


def test_bounded_buffer_drop_oldest():
    buffer = BoundedBuffer(max_size=2, overflow_strategy="drop_oldest")
    buffer.push(StreamEvent(event_id="1", payload={}, occurred_utc="t1"))
    buffer.push(StreamEvent(event_id="2", payload={}, occurred_utc="t2"))
    buffer.push(StreamEvent(event_id="3", payload={}, occurred_utc="t3"))
    drained = buffer.drain(10)
    assert [event.event_id for event in drained] == ["2", "3"]
    assert buffer.dropped == 1


def test_bounded_buffer_drop_newest():
    buffer = BoundedBuffer(max_size=2, overflow_strategy="drop_newest")
    buffer.push(StreamEvent(event_id="1", payload={}, occurred_utc="t1"))
    buffer.push(StreamEvent(event_id="2", payload={}, occurred_utc="t2"))
    buffer.push(StreamEvent(event_id="3", payload={}, occurred_utc="t3"))
    drained = buffer.drain(10)
    assert [event.event_id for event in drained] == ["1", "2"]
    assert buffer.dropped == 1


def test_bounded_buffer_block():
    buffer = BoundedBuffer(max_size=1, overflow_strategy="block")
    buffer.push(StreamEvent(event_id="1", payload={}, occurred_utc="t1"))
    try:
        buffer.push(StreamEvent(event_id="2", payload={}, occurred_utc="t2"))
    except BufferFullError:
        pass
    else:
        raise AssertionError("Expected BufferFullError")


def test_simulated_stream_adapter_backpressure():
    adapter = SimulatedStreamingAdapter(
        SimulatedStreamConfig(
            source_id="sim",
            total_events=10,
            events_per_poll=5,
            max_buffer_size=2,
            overflow_strategy="block",
            max_batch_size=1,
        )
    )
    batch, next_cursor, metadata = adapter.poll(0)
    assert len(batch) == 1
    assert next_cursor <= 2
    assert metadata["backpressure"] is True


def test_build_stream_documents():
    events = [
        StreamEvent(event_id="evt-1", payload={"k": "v"}, occurred_utc="t1"),
        StreamEvent(event_id="evt-2", payload={"k": "v2"}, occurred_utc="t2"),
    ]
    docs = build_stream_documents(events, source="stream_simulated", metadata={"batch": 1})
    assert docs[0]["doc_id"] == "evt-1"
    assert docs[0]["metadata"]["batch"] == 1


def test_streaming_retry_after_backpressure():
    adapter = SimulatedStreamingAdapter(
        SimulatedStreamConfig(
            source_id="sim",
            total_events=3,
            events_per_poll=3,
            max_buffer_size=1,
            overflow_strategy="block",
            max_batch_size=1,
        )
    )
    cursor = 0
    batches = []
    backpressure_flags = []
    for _ in range(3):
        batch, cursor, metadata = adapter.poll(cursor)
        batches.extend(batch)
        backpressure_flags.append(metadata["backpressure"])
        if adapter.is_complete(cursor):
            break
    assert adapter.is_complete(cursor)
    assert {event.event_id for event in batches} == {"sim:0", "sim:1", "sim:2"}
    assert any(backpressure_flags)


def test_streaming_metadata_drop_newest():
    adapter = SimulatedStreamingAdapter(
        SimulatedStreamConfig(
            source_id="sim",
            total_events=5,
            events_per_poll=3,
            max_buffer_size=1,
            overflow_strategy="drop_newest",
            max_batch_size=1,
        )
    )
    batch, cursor, metadata = adapter.poll(0)
    assert len(batch) == 1
    assert cursor == 3
    assert metadata["dropped"] == 2
    assert metadata["generated"] == 3
    assert metadata["backpressure"] is False
    assert metadata["buffer_depth"] == 0


def test_streaming_metadata_drop_oldest():
    adapter = SimulatedStreamingAdapter(
        SimulatedStreamConfig(
            source_id="sim",
            total_events=5,
            events_per_poll=3,
            max_buffer_size=1,
            overflow_strategy="drop_oldest",
            max_batch_size=1,
        )
    )
    batch, cursor, metadata = adapter.poll(0)
    assert len(batch) == 1
    assert cursor == 3
    assert metadata["dropped"] == 2
    assert metadata["generated"] == 3
    assert metadata["backpressure"] is False
    assert metadata["buffer_depth"] == 0
