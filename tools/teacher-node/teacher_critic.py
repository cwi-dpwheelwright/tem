from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import re
from pathlib import Path
from typing import Any, Iterable

from pack_utils import compute_anchor, load_pack


SHA256_PATTERN = re.compile(r"^[a-fA-F0-9]{64}$")
MAX_SCAN_FILES = 1000


@dataclass(frozen=True)
class CriticFinding:
    severity: str
    category: str
    code: str
    message: str
    evidence: dict[str, Any]

    def to_dict(self) -> dict[str, Any]:
        return {
            "severity": self.severity,
            "category": self.category,
            "code": self.code,
            "message": self.message,
            "evidence": self.evidence,
        }


def run_teacher_critic(
    *,
    pack_roots: Iterable[Path] = (),
    artifact_root: Path | None = None,
    vector_root: Path | None = None,
) -> dict[str, Any]:
    pack_roots_tuple = tuple(pack_roots)
    findings: list[CriticFinding] = []
    anchor_index: dict[str, dict[str, str]] = {}

    findings.extend(_validate_pack_anchors(pack_roots_tuple, anchor_index))
    findings.extend(_validate_teacher_artifacts(artifact_root, anchor_index))
    findings.extend(_validate_vector_consistency(vector_root, anchor_index))

    counts = {
        "pass": sum(1 for item in findings if item.severity == "pass"),
        "warning": sum(1 for item in findings if item.severity == "warning"),
        "fail": sum(1 for item in findings if item.severity == "fail"),
    }
    status = "fail" if counts["fail"] else "warning" if counts["warning"] else "pass"
    return {
        "schema_version": "teacher_critic_report.v1",
        "report_id": _report_id(findings),
        "generated_utc": _utc_now(),
        "status": status,
        "summary": counts,
        "inputs": {
            "pack_roots": [str(path) for path in pack_roots_tuple],
            "artifact_root": str(artifact_root) if artifact_root else None,
            "vector_root": str(vector_root) if vector_root else None,
        },
        "findings": [item.to_dict() for item in findings],
    }


def write_teacher_critic_report(report: dict[str, Any], out_path: Path) -> Path:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return out_path


def write_validation_marker(report: dict[str, Any], marker_path: Path) -> Path | None:
    if report.get("status") != "pass":
        return None
    marker_path.parent.mkdir(parents=True, exist_ok=True)
    marker_path.write_text(
        json.dumps(
            {
                "timestamp_utc": report.get("generated_utc"),
                "report_id": report.get("report_id"),
                "status": report.get("status"),
            },
            indent=2,
            sort_keys=True,
        )
        + "\n",
        encoding="utf-8",
    )
    return marker_path


def _validate_pack_anchors(
    pack_roots: tuple[Path, ...],
    anchor_index: dict[str, dict[str, str]],
) -> list[CriticFinding]:
    findings: list[CriticFinding] = []
    if not pack_roots:
        return [
            _finding(
                "warning",
                "pack",
                "pack_roots_not_configured",
                "No pack roots were supplied to the Teacher Critic.",
            )
        ]

    pack_dirs = list(_iter_pack_dirs(pack_roots))
    if not pack_dirs:
        return [
            _finding(
                "fail",
                "pack",
                "pack_roots_empty",
                "Pack roots were supplied, but no pack.json files were found.",
                roots=[str(path) for path in pack_roots],
            )
        ]

    for pack_dir in pack_dirs:
        pack_path = pack_dir / "pack.json"
        anchor_path = pack_dir / "anchor.txt"
        try:
            pack = load_pack(pack_path)
            expected_anchor = compute_anchor(pack_path)
        except Exception as exc:
            findings.append(
                _finding(
                    "fail",
                    "pack",
                    "pack_json_invalid",
                    "pack.json could not be parsed or canonicalized.",
                    path=str(pack_path),
                    error=type(exc).__name__,
                )
            )
            continue

        pack_id = _string_value(pack.get("pack_id") or pack.get("name"))
        pack_version = _string_value(pack.get("pack_version") or pack.get("version"))
        if not pack_id or not pack_version:
            findings.append(
                _finding(
                    "fail",
                    "pack",
                    "pack_identity_missing",
                    "Pack identity fields are required for Teacher validation.",
                    path=str(pack_path),
                    pack_id=pack_id,
                    pack_version=pack_version,
                )
            )

        anchor_index[expected_anchor] = {
            "pack_id": pack_id or "",
            "pack_version": pack_version or "",
            "pack_path": str(pack_path),
        }

        if not anchor_path.exists():
            findings.append(
                _finding(
                    "fail",
                    "pack",
                    "anchor_missing",
                    "anchor.txt is required beside pack.json.",
                    path=str(anchor_path),
                    expected_anchor=expected_anchor,
                )
            )
            continue

        actual_anchor = anchor_path.read_text(encoding="utf-8").strip()
        if actual_anchor != expected_anchor:
            findings.append(
                _finding(
                    "fail",
                    "pack",
                    "anchor_mismatch",
                    "anchor.txt does not match sha256(canonical(pack.json)).",
                    path=str(anchor_path),
                    expected_anchor=expected_anchor,
                    actual_anchor=actual_anchor,
                )
            )
            continue

        findings.append(
            _finding(
                "pass",
                "pack",
                "anchor_verified",
                "Pack anchor matches canonical pack hash.",
                pack_id=pack_id,
                pack_version=pack_version,
                anchor=expected_anchor,
                path=str(pack_path),
            )
        )

    return findings


def _validate_teacher_artifacts(
    artifact_root: Path | None,
    anchor_index: dict[str, dict[str, str]],
) -> list[CriticFinding]:
    if artifact_root is None:
        return [
            _finding(
                "warning",
                "artifact",
                "artifact_root_not_configured",
                "No Teacher artifact root was supplied.",
            )
        ]

    if not artifact_root.exists() or not artifact_root.is_dir():
        return [
            _finding(
                "fail",
                "artifact",
                "artifact_root_unavailable",
                "Teacher artifact root is missing or is not a directory.",
                path=str(artifact_root),
            )
        ]

    json_files = [path for path in _iter_files(artifact_root) if path.suffix.lower() == ".json"]
    findings = [
        _finding(
            "pass",
            "artifact",
            "artifact_root_available",
            "Teacher artifact root is available.",
            path=str(artifact_root),
        )
    ]

    if not json_files:
        findings.append(
            _finding(
                "warning",
                "artifact",
                "artifact_json_not_found",
                "Teacher artifact root contains no JSON artifacts to validate.",
                path=str(artifact_root),
            )
        )
        return findings

    invalid_count = 0
    checked_count = 0
    for path in json_files[:MAX_SCAN_FILES]:
        checked_count += 1
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except Exception as exc:
            invalid_count += 1
            findings.append(
                _finding(
                    "fail",
                    "artifact",
                    "artifact_json_invalid",
                    "Teacher artifact JSON could not be parsed.",
                    path=str(path),
                    error=type(exc).__name__,
                )
            )
            continue
        findings.extend(_validate_artifact_anchor_reference(path, payload, anchor_index))

    if invalid_count == 0:
        findings.append(
            _finding(
                "pass",
                "artifact",
                "artifact_json_valid",
                "Teacher artifact JSON files parsed successfully.",
                checked_count=checked_count,
            )
        )

    return findings


def _validate_vector_consistency(
    vector_root: Path | None,
    anchor_index: dict[str, dict[str, str]],
) -> list[CriticFinding]:
    if vector_root is None:
        return [
            _finding(
                "warning",
                "vector",
                "vector_root_not_configured",
                "No vector or Qdrant export root was supplied.",
            )
        ]

    if not vector_root.exists() or not vector_root.is_dir():
        return [
            _finding(
                "fail",
                "vector",
                "vector_root_unavailable",
                "Vector or Qdrant export root is missing or is not a directory.",
                path=str(vector_root),
            )
        ]

    records = list(_iter_vector_records(vector_root))
    findings: list[CriticFinding] = []
    if not records:
        return [
            _finding(
                "warning",
                "vector",
                "vector_records_not_found",
                "Vector root contains no JSON or JSONL records to validate.",
                path=str(vector_root),
            )
        ]

    checked_anchors = 0
    pack_ids = {
        value["pack_id"]
        for value in anchor_index.values()
        if value.get("pack_id")
    }
    for path, record in records[:MAX_SCAN_FILES]:
        payload = _record_payload(record)
        critic_error = _string_value(payload.get("_critic_error"))
        if critic_error:
            findings.append(
                _finding(
                    "fail",
                    "vector",
                    "vector_export_invalid",
                    "Vector or Qdrant export record could not be parsed.",
                    path=str(path),
                    error=critic_error,
                )
            )
            continue
        anchor = _string_value(
            payload.get("pack_anchor")
            or payload.get("anchor")
            or payload.get("pack_hash")
        )
        pack_id = _string_value(payload.get("pack_id"))
        source_hash = _string_value(payload.get("source_hash") or payload.get("source_sha256"))

        if source_hash and not SHA256_PATTERN.fullmatch(source_hash):
            findings.append(
                _finding(
                    "warning",
                    "vector",
                    "vector_source_hash_invalid",
                    "Vector record has a non-SHA-256 source hash.",
                    path=str(path),
                    source_hash=source_hash,
                )
            )

        if anchor:
            checked_anchors += 1
            if not SHA256_PATTERN.fullmatch(anchor):
                findings.append(
                    _finding(
                        "fail",
                        "vector",
                        "vector_anchor_invalid",
                        "Vector record has an invalid pack anchor format.",
                        path=str(path),
                        pack_anchor=anchor,
                    )
                )
                continue
            if anchor_index and anchor not in anchor_index:
                findings.append(
                    _finding(
                        "fail",
                        "vector",
                        "vector_anchor_unknown",
                        "Vector record references a pack anchor not present in validated packs.",
                        path=str(path),
                        pack_anchor=anchor,
                    )
                )
            continue

        if pack_id and pack_id in pack_ids:
            findings.append(
                _finding(
                    "warning",
                    "vector",
                    "vector_pack_anchor_missing",
                    "Vector record references a known pack without carrying its pack anchor.",
                    path=str(path),
                    pack_id=pack_id,
                )
            )

    if checked_anchors > 0 and not any(item.severity == "fail" for item in findings):
        findings.append(
            _finding(
                "pass",
                "vector",
                "vector_anchors_consistent",
                "Vector or Qdrant export anchors are consistent with validated packs.",
                checked_anchor_count=checked_anchors,
                record_count=len(records),
            )
        )

    if checked_anchors == 0:
        findings.append(
            _finding(
                "warning",
                "vector",
                "vector_pack_anchors_not_found",
                "Vector records did not include pack anchors to validate.",
                record_count=len(records),
            )
        )

    return findings


def _validate_artifact_anchor_reference(
    path: Path,
    payload: Any,
    anchor_index: dict[str, dict[str, str]],
) -> list[CriticFinding]:
    if not isinstance(payload, dict):
        return []
    anchor = _string_value(
        payload.get("anchor")
        or payload.get("pack_anchor")
        or payload.get("artifact_hash")
    )
    if not anchor:
        return []
    if not SHA256_PATTERN.fullmatch(anchor):
        return [
            _finding(
                "fail",
                "artifact",
                "artifact_anchor_invalid",
                "Teacher artifact contains an invalid anchor/hash value.",
                path=str(path),
                anchor=anchor,
            )
        ]
    if anchor_index and anchor not in anchor_index:
        return [
            _finding(
                "warning",
                "artifact",
                "artifact_anchor_not_in_pack_index",
                "Teacher artifact hash is not present in the validated pack index.",
                path=str(path),
                anchor=anchor,
            )
        ]
    return []


def _iter_pack_dirs(roots: tuple[Path, ...]) -> Iterable[Path]:
    seen: set[Path] = set()
    for root in roots:
        if root.is_file() and root.name == "pack.json":
            candidates = [root.parent]
        elif (root / "pack.json").exists():
            candidates = [root]
        elif root.exists() and root.is_dir():
            candidates = [path.parent for path in root.rglob("pack.json")]
        else:
            candidates = []
        for candidate in candidates:
            resolved = candidate.resolve()
            if resolved in seen:
                continue
            seen.add(resolved)
            yield candidate


def _iter_files(root: Path) -> Iterable[Path]:
    count = 0
    for path in root.rglob("*"):
        if path.is_file():
            yield path
            count += 1
        if count >= MAX_SCAN_FILES:
            break


def _iter_vector_records(root: Path) -> Iterable[tuple[Path, dict[str, Any]]]:
    for path in _iter_files(root):
        suffix = path.suffix.lower()
        if suffix == ".jsonl":
            for record in _read_jsonl_records(path):
                yield path, record
        elif suffix == ".json":
            for record in _read_json_records(path):
                yield path, record


def _read_jsonl_records(path: Path) -> Iterable[dict[str, Any]]:
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            yield {"payload": {"_critic_error": "invalid_jsonl_record"}}
            continue
        if isinstance(payload, dict):
            yield payload


def _read_json_records(path: Path) -> Iterable[dict[str, Any]]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        yield {"payload": {"_critic_error": "invalid_json_record"}}
        return
    if isinstance(payload, list):
        for item in payload:
            if isinstance(item, dict):
                yield item
        return
    if isinstance(payload, dict):
        for key in ("points", "records", "vectors", "items"):
            nested = payload.get(key)
            if isinstance(nested, list):
                for item in nested:
                    if isinstance(item, dict):
                        yield item
                return
        yield payload


def _record_payload(record: dict[str, Any]) -> dict[str, Any]:
    payload = record.get("payload")
    if isinstance(payload, dict):
        return payload
    metadata = record.get("metadata")
    if isinstance(metadata, dict):
        return metadata
    return record


def _finding(
    severity: str,
    category: str,
    code: str,
    message: str,
    **evidence: Any,
) -> CriticFinding:
    return CriticFinding(
        severity=severity,
        category=category,
        code=code,
        message=message,
        evidence={key: value for key, value in evidence.items() if value is not None},
    )


def _string_value(value: Any) -> str | None:
    if isinstance(value, str) and value.strip():
        return value.strip()
    return None


def _report_id(findings: list[CriticFinding]) -> str:
    payload = [item.to_dict() for item in findings]
    digest = hashlib.sha256(
        json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()
    return f"teacher-critic-{digest[:16]}"


def _utc_now() -> str:
    return (
        datetime.now(timezone.utc)
        .replace(microsecond=0)
        .isoformat()
        .replace("+00:00", "Z")
    )
