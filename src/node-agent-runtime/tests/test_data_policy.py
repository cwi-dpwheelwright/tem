from __future__ import annotations

from pathlib import Path

from node_agent_runtime.config import DataBoundaryConfig
from node_agent_runtime.data_policy import evaluate_data_boundary


def test_data_boundary_blocks_raw_by_default():
    config = DataBoundaryConfig()
    decision = evaluate_data_boundary({"raw_data": True}, config)
    assert decision.allowed is False
    assert "raw_data_blocked" in decision.reasons


def test_data_boundary_allows_raw_when_configured():
    config = DataBoundaryConfig(allow_raw_data=True)
    decision = evaluate_data_boundary({"raw_data": True}, config)
    assert decision.allowed is True


def test_data_boundary_export_path_allowlist(tmp_path):
    allowed = (tmp_path / "out",)
    allowed[0].mkdir(parents=True, exist_ok=True)
    config = DataBoundaryConfig(allowed_export_paths=allowed)
    decision = evaluate_data_boundary({"export_paths": [str(allowed[0] / "file.txt")]}, config)
    assert decision.allowed is True


def test_data_boundary_export_host_allowlist():
    config = DataBoundaryConfig(allowed_export_hosts=("api.cwi.dev",))
    decision = evaluate_data_boundary({"export_urls": ["https://api.cwi.dev/upload"]}, config)
    assert decision.allowed is True
