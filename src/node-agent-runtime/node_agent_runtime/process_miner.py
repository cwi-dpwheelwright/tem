"""Observe-only local process mining for home-node signals."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import re
from pathlib import Path
from typing import Any, Iterable

from .config import AgentConfig
from .task_runtime import TaskResult

_SAFE_TOKEN_RE = re.compile(r"^[A-Za-z0-9_.:-]{1,64}$")
_DEFAULT_STATE_SUBDIR = "process_miner"
_OBSERVATIONS_FILENAME = "observations.jsonl"
_SUMMARIES_FILENAME = "summaries.jsonl"
_APPROVED_POLICY_STATUSES = {"approved", "allowed"}
_OBSERVATION_TYPES = {"routine", "open_loop", "document", "relationship", "anomaly"}
_UNSAFE_KEYS = {
    "answer",
    "body",
    "conversation",
    "content",
    "document",
    "email",
    "file_content",
    "message",
    "note",
    "prompt",
    "raw",
    "raw_text",
    "text",
    "transcript",
}
_MAX_TAGS = 8
_MAX_OBSERVATIONS = 100


@dataclass(frozen=True)
class ProcessObservation:
    observation_id: str
    observation_type: str
    signal: str
    outcome: str
    count: int
    confidence: float
    tags: tuple[str, ...] = ()
    source_kind: str = "local_signal"
    source_hash: str | None = None
    policy_status: str = "observe"
    timestamp_utc: str = ""
    raw_content_stored: bool = False
    memory_mutated: bool = False
    graph_mutated: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {
            "observation_id": self.observation_id,
            "observation_type": self.observation_type,
            "signal": self.signal,
            "outcome": self.outcome,
            "count": self.count,
            "confidence": self.confidence,
            "tags": list(self.tags),
            "source_kind": self.source_kind,
            "source_hash": self.source_hash,
            "policy_status": self.policy_status,
            "timestamp_utc": self.timestamp_utc,
            "raw_content_stored": self.raw_content_stored,
            "memory_mutated": self.memory_mutated,
            "graph_mutated": self.graph_mutated,
        }


@dataclass(frozen=True)
class ProcessMinerResult:
    status: str
    observations: tuple[ProcessObservation, ...] = ()
    summary: dict[str, Any] | None = None
    reasons: tuple[str, ...] = ()
    storage_path: str | None = None
    raw_content_exported: bool = False
    memory_mutated: bool = False
    graph_mutated: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {
            "status": self.status,
            "observation_count": len(self.observations),
            "observations": [item.to_dict() for item in self.observations],
            "summary": self.summary,
            "reasons": list(self.reasons),
            "storage_path": self.storage_path,
            "raw_content_exported": self.raw_content_exported,
            "memory_mutated": self.memory_mutated,
            "graph_mutated": self.graph_mutated,
        }


@dataclass(frozen=True)
class ProcessMinerSummaryRequest:
    tenant_id: str
    node_id: str
    user_id_hash: str | None = None
    trace_id: str | None = None
    policy_status: str = "observe"
    source_component: str = "local_process_miner"


@dataclass
class LocalProcessMiner:
    state_dir: Path | None
    max_observations: int = _MAX_OBSERVATIONS

    def observe(
        self,
        records: Iterable[dict[str, Any]],
        *,
        policy_status: str = "observe",
        summary_request: ProcessMinerSummaryRequest | None = None,
    ) -> ProcessMinerResult:
        if self.state_dir is None:
            return ProcessMinerResult(
                status="blocked",
                reasons=("state_dir_required",),
            )

        reasons: list[str] = []
        clean_policy_status = _safe_token(policy_status, "observe")
        observations = tuple(
            self._build_observation(record, clean_policy_status)
            for record in list(records)[: self.max_observations]
            if isinstance(record, dict)
        )

        if not observations:
            reasons.append("no_observations")

        storage_path = self._write_observations(observations) if observations else None
        summary = None
        if summary_request is not None:
            summary = self.build_summary(observations, summary_request)
            self._write_summary(summary)
            if not summary["approved_for_export"]:
                reasons.extend(summary["reasons"])

        return ProcessMinerResult(
            status="observed" if observations else "empty",
            observations=observations,
            summary=summary,
            reasons=tuple(dict.fromkeys(reasons)),
            storage_path=str(storage_path) if storage_path else None,
        )

    def build_summary(
        self,
        observations: Iterable[ProcessObservation],
        request: ProcessMinerSummaryRequest,
    ) -> dict[str, Any]:
        items = list(observations)[: self.max_observations]
        policy_status = _safe_token(request.policy_status, "observe")
        approved_for_export = policy_status.lower() in _APPROVED_POLICY_STATUSES
        reasons: list[str] = []
        if not approved_for_export:
            reasons.append("policy_status_not_approved")
        if not items:
            reasons.append("no_exportable_observations")

        return {
            "schema_version": "local_process_miner_summary.v1",
            "tenant_id": _safe_token(request.tenant_id, "unknown_tenant"),
            "user_id_hash": _optional_safe_token(request.user_id_hash),
            "node_id": _safe_token(request.node_id, "unknown_node"),
            "trace_id": _optional_safe_token(request.trace_id),
            "policy_status": policy_status,
            "source_component": _safe_token(request.source_component, "local_process_miner"),
            "approved_for_export": approved_for_export and bool(items),
            "raw_content_exported": False,
            "observations": [
                {
                    "category": item.observation_type,
                    "signal": item.signal,
                    "outcome": item.outcome,
                    "count": item.count,
                    "confidence": item.confidence,
                    "tags": list(item.tags),
                    "metadata": {
                        "source_kind": item.source_kind,
                        "source_hash": item.source_hash or "",
                    },
                }
                for item in items
            ],
            "provenance": {
                "source_hash": _hash_json([item.to_dict() for item in items]),
                "source_id": "local_process_miner",
                "generated_utc": _utc_now(),
            },
            "reasons": reasons,
        }

    def _build_observation(self, record: dict[str, Any], policy_status: str) -> ProcessObservation:
        safe_record = _strip_unsafe_values(record)
        observation_type = _safe_observation_type(
            safe_record.get("type") or safe_record.get("category")
        )
        signal = _safe_token(safe_record.get("signal"), f"{observation_type}_observed")
        outcome = _safe_token(safe_record.get("outcome"), "observed")
        count = _safe_count(safe_record.get("count"))
        confidence = _safe_confidence(safe_record.get("confidence"))
        tags = tuple(
            _safe_token(tag, "")
            for tag in _as_string_list(safe_record.get("tags"))[:_MAX_TAGS]
        )
        tags = tuple(tag for tag in tags if tag)
        source_kind = _safe_token(safe_record.get("source_kind"), "local_signal")
        source_hash = _optional_safe_token(
            safe_record.get("source_hash")
        ) or _hash_json(safe_record)
        timestamp = _utc_now()
        observation_id = _hash_json(
            {
                "observation_type": observation_type,
                "signal": signal,
                "outcome": outcome,
                "source_hash": source_hash,
                "timestamp_utc": timestamp,
            }
        )

        return ProcessObservation(
            observation_id=observation_id,
            observation_type=observation_type,
            signal=signal,
            outcome=outcome,
            count=count,
            confidence=confidence,
            tags=tags,
            source_kind=source_kind,
            source_hash=source_hash,
            policy_status=policy_status,
            timestamp_utc=timestamp,
        )

    def _write_observations(self, observations: tuple[ProcessObservation, ...]) -> Path | None:
        directory = resolve_process_miner_dir(self.state_dir)
        if directory is None:
            return None
        path = directory / _OBSERVATIONS_FILENAME
        _append_jsonl(path, [item.to_dict() for item in observations])
        return path

    def _write_summary(self, summary: dict[str, Any] | None) -> Path | None:
        if summary is None:
            return None
        directory = resolve_process_miner_dir(self.state_dir)
        if directory is None:
            return None
        path = directory / _SUMMARIES_FILENAME
        _append_jsonl(path, [summary])
        return path


class LocalProcessMinerHandler:
    def __init__(self, config: AgentConfig) -> None:
        self._config = config

    def run(self, payload: dict[str, Any]) -> TaskResult:
        records = payload.get("observations")
        if records is None:
            records = payload.get("signals")
        if not isinstance(records, list):
            records = []

        summary_request = None
        if payload.get("emit_summary") is True:
            summary_request = ProcessMinerSummaryRequest(
                tenant_id=(
                    _read_string(payload, "tenant_id")
                    or self._config.control_session.tenant_id
                    or "unknown_tenant"
                ),
                user_id_hash=_read_string(payload, "user_id_hash"),
                node_id=_read_string(payload, "node_id") or self._config.identity.node_id,
                trace_id=_read_string(payload, "trace_id"),
                policy_status=_read_string(payload, "policy_status") or "observe",
            )

        miner = LocalProcessMiner(self._config.storage.state_dir)
        result = miner.observe(
            records,
            policy_status=_read_string(payload, "policy_status") or "observe",
            summary_request=summary_request,
        )
        task_status = "blocked" if result.status == "blocked" else "completed"
        return TaskResult(status=task_status, details=result.to_dict())


def describe_process_miner_capabilities(_config: AgentConfig) -> list[str]:
    if _config.storage.state_dir is None:
        return []
    return ["local_process_miner", "local_process_miner_observe_only"]


def resolve_process_miner_dir(state_dir: Path | None) -> Path | None:
    if state_dir is None:
        return None
    return state_dir / _DEFAULT_STATE_SUBDIR


def _append_jsonl(path: Path, items: Iterable[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        for item in items:
            handle.write(json.dumps(item, sort_keys=True, separators=(",", ":")))
            handle.write("\n")


def _strip_unsafe_values(value: dict[str, Any]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, item in value.items():
        key_text = str(key)
        if _is_unsafe_key(key_text):
            continue
        if isinstance(item, (str, int, float, bool)) or item is None:
            result[key_text] = item
        elif isinstance(item, list):
            result[key_text] = [
                entry for entry in item if isinstance(entry, (str, int, float, bool))
            ]
        elif isinstance(item, dict):
            result[key_text] = {
                str(child_key): child_value
                for child_key, child_value in item.items()
                if not _is_unsafe_key(str(child_key))
                and isinstance(child_value, (str, int, float, bool))
            }
    return result


def _is_unsafe_key(value: str) -> bool:
    lowered = value.strip().lower()
    return any(key == lowered or key in lowered for key in _UNSAFE_KEYS)


def _safe_observation_type(value: Any) -> str:
    token = _safe_token(value, "routine")
    return token if token in _OBSERVATION_TYPES else "routine"


def _safe_token(value: Any, fallback: str) -> str:
    if not isinstance(value, str):
        return fallback
    token = value.strip()
    if _SAFE_TOKEN_RE.fullmatch(token):
        return token
    return fallback


def _optional_safe_token(value: Any) -> str | None:
    token = _safe_token(value, "")
    return token or None


def _safe_count(value: Any) -> int:
    if isinstance(value, bool):
        return 1
    if isinstance(value, int):
        return max(1, min(value, 100000))
    return 1


def _safe_confidence(value: Any) -> float:
    if isinstance(value, (int, float)) and not isinstance(value, bool):
        return round(max(0.0, min(float(value), 1.0)), 4)
    return 0.5


def _as_string_list(value: Any) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [item for item in value if isinstance(item, str)]
    return []


def _read_string(payload: dict[str, Any], key: str) -> str | None:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        return None
    return value.strip()


def _hash_json(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), default=str).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
