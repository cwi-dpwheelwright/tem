import json
import os
from typing import Dict, Iterable, Optional


def resolve_tenant_id(input_data: Dict[str, object]) -> Optional[str]:
    tenant = _as_str(input_data.get("tenant_id"))
    if tenant:
        return tenant
    metadata = input_data.get("metadata") if isinstance(input_data.get("metadata"), dict) else {}
    tenant = _as_str(metadata.get("tenant_id")) if isinstance(metadata, dict) else None
    if tenant:
        return tenant
    return _env_first("TEACHER_NODE_TENANT_ID", "CWI_TENANT_ID")


def build_artifact_payload(
    tenant_id: str,
    artifact_id: str,
    artifact_type: str,
    version: str,
    artifact_hash: str,
    title: Optional[str] = None,
    provenance_refs: Optional[Iterable[str]] = None,
    metadata: Optional[Dict[str, str]] = None,
) -> Dict[str, object]:
    payload: Dict[str, object] = {
        "tenant_id": tenant_id,
        "artifact_id": artifact_id,
        "artifact_type": artifact_type,
        "version": version,
        "hash": artifact_hash,
        "provenance_refs": list(provenance_refs or []),
    }
    if title:
        payload["title"] = title
    if metadata:
        payload["metadata"] = {key: str(value) for key, value in metadata.items()}
    return payload


def maybe_register_artifact(payload: Dict[str, object]) -> bool:
    orchestrator = os.getenv("ORCHESTRATOR_URL")
    endpoint = os.getenv("TEACHER_NODE_ARTIFACT_REGISTRY_ENDPOINT", "/api/artifacts/register")
    if not orchestrator or not endpoint:
        return False

    token = _env_first("TEACHER_NODE_ARTIFACT_REGISTRY_TOKEN", "CWI_AUTH_TOKEN")
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"

    import urllib.request

    data = json.dumps(payload).encode("utf-8")
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
        return False
    return True


def _env_first(*keys: str) -> Optional[str]:
    for key in keys:
        value = os.getenv(key)
        if value:
            return value
    return None


def _as_str(value: object) -> Optional[str]:
    if value is None:
        return None
    text = str(value).strip()
    return text or None
