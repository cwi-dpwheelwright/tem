from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List


@dataclass(frozen=True)
class ReloadResult:
    changed: bool
    changed_paths: List[str]


class CertificateWatcher:
    def __init__(self, paths: Iterable[str | None]) -> None:
        self._paths: Dict[str, float] = {}
        for path in paths:
            if not path:
                continue
            candidate = Path(path)
            self._paths[str(candidate)] = self._mtime(candidate)

    def check(self) -> ReloadResult:
        changed: List[str] = []
        for path_str in list(self._paths.keys()):
            path = Path(path_str)
            current = self._mtime(path)
            previous = self._paths.get(path_str)
            if current != previous:
                self._paths[path_str] = current
                changed.append(path_str)
        return ReloadResult(changed=bool(changed), changed_paths=changed)

    @staticmethod
    def _mtime(path: Path) -> float:
        if not path.exists():
            return -1.0
        return path.stat().st_mtime
