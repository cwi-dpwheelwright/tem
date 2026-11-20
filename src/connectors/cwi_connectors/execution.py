from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable, Dict, Protocol


@dataclass(frozen=True)
class ServiceError:
    code: str
    message: str


@dataclass(frozen=True)
class ServiceResponse:
    ok: bool
    status_code: int
    data: Dict[str, Any] | None = None
    error: ServiceError | None = None

    @staticmethod
    def success(data: Dict[str, Any] | None = None, status_code: int = 200) -> "ServiceResponse":
        return ServiceResponse(ok=True, status_code=status_code, data=data, error=None)

    @staticmethod
    def fail(code: str, message: str, status_code: int = 500) -> "ServiceResponse":
        return ServiceResponse(ok=False, status_code=status_code, data=None, error=ServiceError(code=code, message=message))


class DomainServiceBase:
    def execute(self, action: Callable[[], ServiceResponse]) -> ServiceResponse:
        try:
            return action()
        except Exception as exc:  # pragma: no cover - safety net
            return ServiceResponse.fail(code=exc.__class__.__name__, message="Unhandled domain error", status_code=500)


class Handler(Protocol):
    def handle(self, payload: Dict[str, Any]) -> ServiceResponse:
        ...


Decorator = Callable[[Handler], Handler]


class ChainHandler:
    def __init__(self, handler: Handler, *decorators: Decorator) -> None:
        current = handler
        for decorator in decorators:
            current = decorator(current)
        self._handler = current

    def handle(self, payload: Dict[str, Any]) -> ServiceResponse:
        return self._handler.handle(payload)
