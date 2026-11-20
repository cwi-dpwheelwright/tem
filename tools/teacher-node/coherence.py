import datetime as dt
import json
import os
import re
from pathlib import Path
from typing import Any, Dict, List, Optional


PASS_PHRASES = re.compile(r"\b(pass|successful|succeeded|no issues)\b", re.IGNORECASE)
FAIL_PHRASES = re.compile(r"\b(fail|failed|errors|issues detected)\b", re.IGNORECASE)


def check_coherence(structured: Dict[str, Any], narrative: str) -> Dict[str, Any]:
    conflicts: List[Dict[str, Any]] = []
    status = structured.get("summary", {}).get("overall_status", "unknown")

    if status == "fail" and PASS_PHRASES.search(narrative):
        conflicts.append({
            "code": "status_mismatch",
            "severity": "high",
            "message": "Structured status is fail but narrative implies pass.",
        })

    if status == "pass" and FAIL_PHRASES.search(narrative):
        conflicts.append({
            "code": "status_mismatch",
            "severity": "high",
            "message": "Structured status is pass but narrative implies failure.",
        })

    findings = structured.get("findings", []) or []
    if findings and PASS_PHRASES.search(narrative):
        conflicts.append({
            "code": "findings_mismatch",
            "severity": "medium",
            "message": "Narrative claims no issues but structured findings exist.",
        })

    status_value = "pass"
    if conflicts:
        status_value = "fail" if any(c["severity"] == "high" for c in conflicts) else "warn"

    return {
        "schema_version": "v1",
        "generated_utc": _utc_now(),
        "status": status_value,
        "conflict_count": len(conflicts),
        "conflicts": conflicts,
    }


def write_coherence_report(report: Dict[str, Any], out_path: Path) -> Dict[str, Any]:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def build_escalation_request(
    structured: Dict[str, Any],
    coherence_report: Dict[str, Any],
    reason: str,
) -> Dict[str, Any]:
    return {
        "schema_version": "v1",
        "requested_utc": _utc_now(),
        "pack_id": structured.get("pack_id"),
        "pack_version": structured.get("pack_version"),
        "run_id": structured.get("run_id"),
        "reason": reason,
        "coherence": coherence_report,
    }


def write_escalation_request(request: Dict[str, Any], out_path: Path) -> None:
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(request, indent=2) + "\n", encoding="utf-8")


def maybe_post_escalation_request(request: Dict[str, Any]) -> None:
    endpoint = os.getenv("TEACHER_NODE_APPROVAL_ENDPOINT")
    orchestrator = os.getenv("ORCHESTRATOR_URL")
    if not endpoint or not orchestrator:
        return

    import urllib.request

    data = json.dumps(request).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    token = os.getenv("TEACHER_NODE_APPROVAL_TOKEN") or os.getenv("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(
        url=f"{orchestrator.rstrip('/')}{endpoint}",
        data=data,
        headers=headers,
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as _resp:
            pass
    except Exception:
        return


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
