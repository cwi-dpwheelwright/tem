import datetime as dt
import json
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional

from reports import build_manifest


def generate_reconciliation_report(input_data: Dict[str, Any]) -> Dict[str, Any]:
    report_id = input_data.get("report_id") or f"recon-{uuid.uuid4().hex[:8]}"
    tenant_id = input_data.get("tenant_id") or "unknown"
    run_id = input_data.get("run_id") or uuid.uuid4().hex
    status = input_data.get("status") or "review"
    summary = input_data.get("summary") or "Reconciliation summary pending."
    conflicts = list(input_data.get("conflicts") or [])
    discrepancies = list(input_data.get("discrepancies") or [])

    structured = {
        "run_id": run_id,
        "conflict_count": len(conflicts),
        "discrepancy_count": len(discrepancies),
        "conflicts": conflicts,
        "discrepancy_ids": [item.get("discrepancy_id") for item in discrepancies if item.get("discrepancy_id")],
        "metrics": input_data.get("metrics") or {},
    }

    narrative = render_reconciliation_narrative(run_id, conflicts, discrepancies, summary)
    coherence_score = _derive_coherence(len(conflicts), len(discrepancies))

    return {
        "report_id": report_id,
        "tenant_id": tenant_id,
        "run_id": run_id,
        "report_type": "reconciliation",
        "generated_utc": _utc_now(),
        "title": input_data.get("title") or "Reconciliation Report",
        "status": status,
        "summary": summary,
        "coherence_score": coherence_score,
        "conflict_count": len(conflicts),
        "narrative": narrative,
        "structured": structured,
        "sources": input_data.get("sources") or [],
        "metadata": input_data.get("metadata") or {},
    }


def render_reconciliation_narrative(
    run_id: str,
    conflicts: List[Dict[str, Any]],
    discrepancies: List[Dict[str, Any]],
    summary: str,
) -> str:
    lines = [
        "# Reconciliation Report",
        "",
        f"Run: {run_id}",
        f"Conflicts: {len(conflicts)}",
        f"Discrepancies: {len(discrepancies)}",
        "",
        summary,
        "",
    ]
    if conflicts:
        lines.append("## Conflicts")
        for conflict in conflicts[:10]:
            lines.append(f"- {conflict.get('conflict_id') or conflict.get('id') or 'conflict'}")
        lines.append("")
    if discrepancies:
        lines.append("## Discrepancies")
        for disc in discrepancies[:10]:
            lines.append(f"- {disc.get('discrepancy_id') or disc.get('id') or 'discrepancy'}")
        lines.append("")
    return "\n".join(lines).strip() + "\n"


def write_reconciliation_bundle(input_data: Dict[str, Any], out_dir: Path) -> Dict[str, Path]:
    out_dir.mkdir(parents=True, exist_ok=True)
    report = generate_reconciliation_report(input_data)

    report_json = out_dir / "reconciliation-report.json"
    report_md = out_dir / "reconciliation-report.md"
    report_json.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    report_md.write_text(report["narrative"], encoding="utf-8")

    discrepancy_path = None
    discrepancies = list(input_data.get("discrepancies") or [])
    if discrepancies:
        discrepancy_path = out_dir / "discrepancy-events.jsonl"
        with discrepancy_path.open("w", encoding="utf-8") as handle:
            for item in discrepancies:
                handle.write(json.dumps(item) + "\n")

    manifest_items = [report_json, report_md]
    if discrepancy_path:
        manifest_items.append(discrepancy_path)
    manifest = build_manifest(manifest_items)
    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")

    return {
        "report_json": report_json,
        "report_md": report_md,
        "manifest": manifest_path,
        "discrepancies": discrepancy_path,
    }


def _derive_coherence(conflicts: int, discrepancies: int) -> float:
    penalty = min(1.0, 0.05 * conflicts + 0.02 * discrepancies)
    return round(max(0.0, 1.0 - penalty), 3)


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
