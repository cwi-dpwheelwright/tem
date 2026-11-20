from __future__ import annotations

import json
from pathlib import Path
from typing import Any


def find_fixtures_root() -> Path:
    current = Path(__file__).resolve()
    for _ in range(8):
        candidate = current / "contracts" / "fixtures"
        if candidate.exists():
            return candidate
        current = current.parent
    raise FileNotFoundError("Unable to locate contracts/fixtures")


def load_fixture(relative_path: str) -> Any:
    root = find_fixtures_root()
    path = root / relative_path
    data = path.read_text(encoding="utf-8-sig")
    return json.loads(data)


def resolve_fixture_path(relative_path: str) -> Path:
    root = find_fixtures_root()
    return root / relative_path
