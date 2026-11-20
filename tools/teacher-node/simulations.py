from dataclasses import dataclass
from typing import Dict, List


@dataclass(frozen=True)
class SimulationResult:
    scenario: str
    status: str
    details: Dict[str, int]


class DeliveryQueue:
    def __init__(self, max_inflight: int = 5) -> None:
        self.max_inflight = max_inflight
        self.buffer: List[str] = []
        self.inflight = 0

    def enqueue(self, item: str) -> None:
        self.buffer.append(item)

    def flush(self, online: bool = True) -> str:
        if not online:
            return "offline"
        while self.buffer:
            if self.inflight >= self.max_inflight:
                return "backpressure"
            self.buffer.pop(0)
            self.inflight += 1
        return "drained"

    def reset(self) -> None:
        self.inflight = 0


def simulate_network_partition(queue: DeliveryQueue) -> SimulationResult:
    queue.enqueue("event-1")
    status = queue.flush(online=False)
    return SimulationResult(
        scenario="network_partition",
        status="degraded" if status == "offline" else "unexpected",
        details={"buffered": len(queue.buffer)},
    )


def simulate_offline_mode(queue: DeliveryQueue) -> SimulationResult:
    for idx in range(3):
        queue.enqueue(f"event-{idx}")
    status = queue.flush(online=False)
    return SimulationResult(
        scenario="offline_mode",
        status="queued" if status == "offline" else "unexpected",
        details={"buffered": len(queue.buffer)},
    )


def simulate_backpressure(queue: DeliveryQueue) -> SimulationResult:
    for idx in range(queue.max_inflight + 2):
        queue.enqueue(f"event-{idx}")
    queue.reset()
    status = queue.flush(online=True)
    return SimulationResult(
        scenario="backpressure",
        status="backpressure" if status == "backpressure" else "unexpected",
        details={"remaining": len(queue.buffer)},
    )


def run_simulations() -> List[SimulationResult]:
    queue = DeliveryQueue(max_inflight=3)
    results = [
        simulate_network_partition(queue),
        simulate_offline_mode(queue),
        simulate_backpressure(queue),
    ]
    return results
