import datetime as dt
import json
import os
import uuid
from typing import Any, Dict, List


def evaluate_governance(template: Dict[str, Any], input_data: Dict[str, Any]) -> Dict[str, Any]:
    constraints = template.get("constraints", {}) or {}
    classification = (template.get("classification") or "").lower()
    requires = bool(constraints.get("requires_approval")) or classification in ("restricted", "controlled")
    reason = constraints.get("approval_reason") or "restricted_template"
    approvals = list(input_data.get("approvals") or [])
    if requires and not approvals:
        approvals.append({
            "approval_id": f"APR-{uuid.uuid4().hex[:8]}",
            "status": "pending",
        })
    return {
        "requires_approval": requires,
        "approval_reason": reason,
        "approvals": approvals,
    }


def build_approval_request(document: Dict[str, Any], template: Dict[str, Any], reason: str) -> Dict[str, Any]:
    return {
        "schema_version": "v1",
        "requested_utc": _utc_now(),
        "document_id": document.get("document_id"),
        "tenant_id": document.get("tenant_id"),
        "template_id": template.get("template_id"),
        "template_version": template.get("version"),
        "reason": reason,
    }


def maybe_post_approval_request(request: Dict[str, Any]) -> None:
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
