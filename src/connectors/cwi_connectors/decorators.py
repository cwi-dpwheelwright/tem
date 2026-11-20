from __future__ import annotations

import time
from typing import Callable

from .execution import Handler, ServiceResponse


def retry_decorator(max_attempts: int = 3, backoff_seconds: float = 0.5) -> Callable[[Handler], Handler]:
    def wrap(handler: Handler) -> Handler:
        class Wrapped:
            def handle(self, payload):
                last = None
                for attempt in range(max_attempts):
                    response = handler.handle(payload)
                    if response.ok:
                        return response
                    last = response
                    time.sleep(backoff_seconds * (attempt + 1))
                return last or ServiceResponse.fail("retry_exhausted", "retries exhausted")

        return Wrapped()

    return wrap


def telemetry_decorator(label: str) -> Callable[[Handler], Handler]:
    def wrap(handler: Handler) -> Handler:
        class Wrapped:
            def handle(self, payload):
                start = time.time()
                response = handler.handle(payload)
                elapsed = time.time() - start
                payload.setdefault("telemetry", []).append(
                    {"label": label, "elapsed_ms": int(elapsed * 1000), "ok": response.ok}
                )
                return response

        return Wrapped()

    return wrap
