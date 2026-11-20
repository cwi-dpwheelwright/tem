from __future__ import annotations

from collections import deque
from dataclasses import dataclass
from typing import Any, Deque, Dict, Iterable, List

from .state_store import utc_now


class BufferFullError(RuntimeError):
    pass


@dataclass(frozen=True)
class StreamEvent:
    event_id: str
    payload: Dict[str, Any]
    occurred_utc: str


class BoundedBuffer:
    def __init__(self, max_size: int, overflow_strategy: str = "drop_oldest") -> None:
        if max_size < 1:
            raise ValueError("max_size must be >= 1")
        if overflow_strategy not in {"drop_oldest", "drop_newest", "block"}:
            raise ValueError("overflow_strategy must be drop_oldest, drop_newest, or block")
        self._max_size = max_size
        self._strategy = overflow_strategy
        self._items: Deque[StreamEvent] = deque()
        self.dropped = 0

    def push(self, event: StreamEvent) -> bool:
        if len(self._items) < self._max_size:
            self._items.append(event)
            return True
        if self._strategy == "drop_oldest":
            self._items.popleft()
            self._items.append(event)
            self.dropped += 1
            return True
        if self._strategy == "drop_newest":
            self.dropped += 1
            return False
        raise BufferFullError("buffer is full")

    def drain(self, max_items: int) -> List[StreamEvent]:
        batch: List[StreamEvent] = []
        while self._items and len(batch) < max_items:
            batch.append(self._items.popleft())
        return batch

    def depth(self) -> int:
        return len(self._items)


@dataclass(frozen=True)
class SimulatedStreamConfig:
    source_id: str
    total_events: int
    events_per_poll: int
    max_buffer_size: int
    overflow_strategy: str
    max_batch_size: int


class SimulatedStreamingAdapter:
    def __init__(self, config: SimulatedStreamConfig) -> None:
        self.config = config
        self.buffer = BoundedBuffer(config.max_buffer_size, config.overflow_strategy)

    def poll(self, start_index: int) -> tuple[List[StreamEvent], int, Dict[str, Any]]:
        generated = 0
        next_index = start_index
        backpressure = False
        for index in range(start_index, min(self.config.total_events, start_index + self.config.events_per_poll)):
            event = StreamEvent(
                event_id=f"{self.config.source_id}:{index}",
                payload={"event_index": index, "source_id": self.config.source_id},
                occurred_utc=utc_now(),
            )
            try:
                accepted = self.buffer.push(event)
            except BufferFullError:
                backpressure = True
                break
            generated += 1
            next_index = index + 1
            if not accepted:
                continue
        batch = self.buffer.drain(self.config.max_batch_size)
        metadata = {
            "generated": generated,
            "dropped": self.buffer.dropped,
            "backpressure": backpressure,
            "buffer_depth": self.buffer.depth(),
        }
        return batch, next_index, metadata

    def remaining(self, cursor: int) -> int:
        return max(0, self.config.total_events - cursor)

    def is_complete(self, cursor: int) -> bool:
        return cursor >= self.config.total_events


def build_stream_documents(
    events: Iterable[StreamEvent],
    source: str,
    metadata: Dict[str, Any] | None = None,
) -> List[Dict[str, Any]]:
    docs: List[Dict[str, Any]] = []
    extra = metadata or {}
    for event in events:
        docs.append(
            {
                "doc_id": event.event_id,
                "title": f"Stream event {event.event_id}",
                "content": f"Event payload: {event.payload}",
                "source": source,
                "metadata": {**event.payload, **extra, "occurred_utc": event.occurred_utc},
            }
        )
    return docs
