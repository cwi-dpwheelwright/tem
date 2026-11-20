"""Job runner abstraction for sandboxed tasks."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Protocol
import subprocess
import time

from .config import ConfigError, JobRunnerConfig


@dataclass(frozen=True)
class JobSpec:
    command: list[str]
    timeout_seconds: int | None = None
    cwd: Path | None = None


@dataclass(frozen=True)
class JobResult:
    status: str
    exit_code: int | None
    stdout: str
    stderr: str
    duration_seconds: float


class JobRunner(Protocol):
    def run(self, spec: JobSpec) -> JobResult:
        ...


class SubprocessJobRunner:
    def __init__(self, config: JobRunnerConfig) -> None:
        self._config = config

    def run(self, spec: JobSpec) -> JobResult:
        if not spec.command:
            raise ConfigError("job_runner command required")
        command_name = spec.command[0]
        if self._config.allow_commands and command_name not in self._config.allow_commands:
            raise ConfigError("job_runner command not allowed")

        timeout = spec.timeout_seconds or self._config.default_timeout_seconds
        cwd = spec.cwd or self._config.working_dir
        start = time.time()
        try:
            completed = subprocess.run(
                spec.command,
                cwd=str(cwd) if cwd else None,
                capture_output=True,
                text=True,
                timeout=timeout,
                check=False,
            )
        except subprocess.TimeoutExpired as exc:
            duration = time.time() - start
            return JobResult(
                status="timeout",
                exit_code=None,
                stdout=exc.stdout or "",
                stderr=exc.stderr or "",
                duration_seconds=duration,
            )
        duration = time.time() - start
        status = "ok" if completed.returncode == 0 else "error"
        return JobResult(
            status=status,
            exit_code=completed.returncode,
            stdout=completed.stdout or "",
            stderr=completed.stderr or "",
            duration_seconds=duration,
        )
