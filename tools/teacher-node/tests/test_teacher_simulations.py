import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from simulations import run_simulations  # noqa: E402


def test_simulation_scenarios():
    results = run_simulations()
    scenarios = {result.scenario: result.status for result in results}
    assert scenarios["network_partition"] == "degraded"
    assert scenarios["offline_mode"] == "queued"
    assert scenarios["backpressure"] == "backpressure"
