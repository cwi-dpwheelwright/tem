import datetime as dt
import hashlib
import json
import uuid
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional


SEVERITY_ORDER = {
    "low": 1,
    "medium": 2,
    "high": 3,
    "critical": 4,
}


def generate_structured_report(input_data: Dict[str, Any]) -> Dict[str, Any]:
    pack_id = input_data.get("pack_id") or "unknown"
    pack_version = input_data.get("pack_version") or "0.0.0"
    run_id = input_data.get("run_id") or uuid.uuid4().hex
    metrics = input_data.get("metrics", {}) or {}
    findings = list(input_data.get("findings", []) or [])
    governance = input_data.get("governance", {}) or {}

    summary = input_data.get("summary", {}) or {}
    overall_status = summary.get("overall_status")
    if not overall_status:
        overall_status = _derive_status(findings)

    score = summary.get("score")
    if score is None:
        score = _derive_score(metrics, findings)

    return {
        "schema_version": "v1",
        "report_id": uuid.uuid4().hex,
        "pack_id": pack_id,
        "pack_version": pack_version,
        "run_id": run_id,
        "generated_utc": _utc_now(),
        "summary": {
            "overall_status": overall_status,
            "score": score,
            "finding_count": len(findings),
        },
        "metrics": metrics,
        "findings": findings,
        "governance": governance,
        "inputs": input_data.get("inputs", {}),
    }


def render_narrative(report: Dict[str, Any]) -> str:
    summary = report.get("summary", {})
    status = summary.get("overall_status", "unknown")
    score = summary.get("score", "n/a")
    findings = report.get("findings", []) or []
    lines = [
        f"# Teacher Node Evaluation Report",
        f"",
        f"Pack: {report.get('pack_id')} ({report.get('pack_version')})",
        f"Run: {report.get('run_id')}",
        f"Generated: {report.get('generated_utc')}",
        f"",
        f"Overall status: **{status}**",
        f"Score: **{score}**",
        f"Findings: **{len(findings)}**",
        f"",
    ]

    if findings:
        lines.append("## Findings")
        for entry in findings:
            sev = entry.get("severity", "unknown")
            msg = entry.get("message", "")
            lines.append(f"- [{sev}] {msg}")
        lines.append("")

    metrics = report.get("metrics", {}) or {}
    if metrics:
        lines.append("## Metrics")
        for key, value in metrics.items():
            lines.append(f"- {key}: {value}")
        lines.append("")

    governance = report.get("governance", {}) or {}
    if governance:
        lines.append("## Governance")
        for key, value in governance.items():
            lines.append(f"- {key}: {value}")
        lines.append("")

    return "\n".join(lines).strip() + "\n"


def write_report_bundle(
    input_data: Dict[str, Any],
    out_dir: Path,
    narrative: Optional[str] = None,
) -> Dict[str, Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    structured = generate_structured_report(input_data)
    if narrative is None:
        narrative = render_narrative(structured)

    report_json = out_dir / "report.json"
    report_md = out_dir / "report.md"
    report_json.write_text(json.dumps(structured, indent=2) + "\n", encoding="utf-8")
    report_md.write_text(narrative, encoding="utf-8")

    manifest_path = out_dir / "manifest.json"
    manifest = build_manifest([report_json, report_md])
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    return {
        "report_json": report_json,
        "report_md": report_md,
        "manifest": manifest_path,
    }


def build_manifest(paths: Iterable[Path]) -> Dict[str, Any]:
    entries: List[Dict[str, Any]] = []
    for path in paths:
        entries.append({
            "name": path.name,
            "sha256": compute_sha256(path),
        })

    return {
        "schema_version": "v1",
        "generated_utc": _utc_now(),
        "artifacts": entries,
    }


def compute_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8192), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _derive_status(findings: Iterable[Dict[str, Any]]) -> str:
    max_sev = 0
    for finding in findings:
        sev = str(finding.get("severity", "")).lower()
        max_sev = max(max_sev, SEVERITY_ORDER.get(sev, 0))
    if max_sev >= SEVERITY_ORDER["high"]:
        return "fail"
    if max_sev >= SEVERITY_ORDER["medium"]:
        return "warn"
    return "pass"


def _derive_score(metrics: Dict[str, Any], findings: List[Dict[str, Any]]) -> float:
    base = 1.0
    base -= min(0.5, 0.1 * len(findings))
    if metrics:
        perf = metrics.get("accuracy") or metrics.get("quality")
        if isinstance(perf, (int, float)):
            base = (base + float(perf)) / 2
    return max(0.0, round(base, 3))


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
