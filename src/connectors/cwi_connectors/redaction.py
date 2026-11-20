from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Iterable, List


@dataclass(frozen=True)
class RedactionRule:
    pattern: str
    replacement: str = "[REDACTED]"


def apply_redactions(text: str, tokens: Iterable[str], rules: Iterable[RedactionRule]) -> str:
    redacted = text
    for token in tokens:
        if token:
            redacted = redacted.replace(token, "[REDACTED]")
    for rule in rules:
        redacted = re.sub(rule.pattern, rule.replacement, redacted)
    return redacted


def default_rules() -> List[RedactionRule]:
    return [
        RedactionRule(pattern=r"\b\d{3}-\d{2}-\d{4}\b"),
        RedactionRule(pattern=r"\b\d{16}\b"),
    ]
