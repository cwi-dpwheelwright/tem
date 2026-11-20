from __future__ import annotations

import random
import time
from dataclasses import dataclass


@dataclass
class BackoffPolicy:
    base_seconds: float = 1.0
    max_seconds: float = 60.0
    multiplier: float = 2.0
    jitter: float = 0.1

    def next_delay(self, attempt: int) -> float:
        raw = min(self.base_seconds * (self.multiplier ** attempt), self.max_seconds)
        jitter_range = raw * self.jitter
        return max(0.0, raw + random.uniform(-jitter_range, jitter_range))


class CircuitBreaker:
    def __init__(self, failure_threshold: int = 3, recovery_seconds: float = 30.0):
        self.failure_threshold = failure_threshold
        self.recovery_seconds = recovery_seconds
        self.failure_count = 0
        self.opened_at: float | None = None

    def record_success(self) -> None:
        self.failure_count = 0
        self.opened_at = None

    def record_failure(self) -> None:
        self.failure_count += 1
        if self.failure_count >= self.failure_threshold:
            self.opened_at = time.time()

    def allow_request(self) -> bool:
        if self.opened_at is None:
            return True
        return (time.time() - self.opened_at) >= self.recovery_seconds

    def status(self) -> str:
        if self.opened_at is None:
            return "closed"
        if self.allow_request():
            return "half-open"
        return "open"
