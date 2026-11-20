from __future__ import annotations

import sys

from node_agent_runtime.config import JobRunnerConfig, ConfigError
from node_agent_runtime.job_runner import JobSpec, SubprocessJobRunner


def test_job_runner_allows_command():
    executable = sys.executable
    config = JobRunnerConfig(allow_commands=(executable,), default_timeout_seconds=5)
    runner = SubprocessJobRunner(config)
    result = runner.run(JobSpec(command=[executable, "-c", "print('ok')"]))
    assert result.status == "ok"


def test_job_runner_blocks_disallowed():
    config = JobRunnerConfig(allow_commands=("python",), default_timeout_seconds=5)
    runner = SubprocessJobRunner(config)
    try:
        runner.run(JobSpec(command=["cmd", "/c", "echo"]))
        assert False, "expected disallow"
    except ConfigError:
        assert True
