"""Task runtime with execution guard."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Protocol

from .config import AgentConfig
from .data_policy import evaluate_data_boundary
from .execution_guard import evaluate_execution
from .priority import extract_priority


@dataclass(frozen=True)
class TaskResult:
    status: str
    details: dict[str, Any]


class TaskHandler(Protocol):
    def run(self, payload: dict[str, Any]) -> TaskResult:
        ...


class TaskRuntime:
    def __init__(self, config: AgentConfig) -> None:
        self._config = config
        self._handlers: dict[str, TaskHandler] = {}

    def register(self, task_type: str, handler: TaskHandler) -> None:
        self._handlers[task_type] = handler

    def run(self, task_type: str, payload: dict[str, Any]) -> TaskResult:
        decision = evaluate_execution(self._config)
        if not decision.allowed:
            return TaskResult(
                status="blocked",
                details={
                    "reasons": decision.reasons,
                    "activation_valid": decision.activation_valid,
                    "kill_switch_enabled": decision.kill_switch_enabled,
                },
            )
        data_decision = evaluate_data_boundary(payload, self._config.data_boundary)
        if not data_decision.allowed:
            return TaskResult(
                status="blocked",
                details={
                    "reasons": data_decision.reasons,
                    "details": data_decision.details,
                },
            )
        priority = extract_priority(payload)
        if self._config.priority.survival_mode and priority.level < self._config.priority.min_priority_level:
            return TaskResult(
                status="blocked",
                details={
                    "reasons": ["survival_mode_suppressed"],
                    "priority": {"label": priority.label, "level": priority.level},
                },
            )
        handler = self._handlers.get(task_type)
        if handler is None:
            return TaskResult(status="unknown_task", details={"task_type": task_type})
        return handler.run(payload)
