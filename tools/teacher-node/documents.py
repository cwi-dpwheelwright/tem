from __future__ import annotations

import datetime as dt
import json
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional


def render_document_content(template: Dict[str, Any], mapped_fields: Dict[str, Any]) -> str:
    lines: List[str] = [f"# {template.get('title', 'Document')}", ""]
    for section in template.get("sections", []) or []:
        title = section.get("title", "Section")
        lines.append(f"## {title}")
        for field in section.get("fields", []) or []:
            field_id = field.get("field_id")
            label = field.get("label") or field_id or "Field"
            value = mapped_fields.get(field_id)
            if value is None:
                value = field.get("default")
            lines.append(f"### {label}")
            field_type = field.get("type")
            if field_type == "list" and isinstance(value, str):
                value = [item for item in value.splitlines() if item.strip()]
            if isinstance(value, list):
                for item in value:
                    lines.append(f"- {item}")
            else:
                lines.append(str(value) if value is not None else "")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def build_procedure_document(
    template: Dict[str, Any],
    mapped_fields: Dict[str, Any],
    input_data: Dict[str, Any],
    approvals: Optional[List[Dict[str, Any]]] = None,
) -> Dict[str, Any]:
    document_id = input_data.get("document_id") or f"doc-{uuid.uuid4().hex[:10]}"
    tenant_id = input_data.get("tenant_id") or "unknown"
    status = input_data.get("status") or "draft"
    summary = input_data.get("summary") or _summarize(mapped_fields)
    content = render_document_content(template, mapped_fields)

    metadata = dict(input_data.get("metadata") or {})
    metadata.setdefault("template_id", template.get("template_id"))
    metadata.setdefault("template_version", template.get("version"))

    return {
        "document_id": document_id,
        "tenant_id": tenant_id,
        "generated_utc": _utc_now(),
        "status": status,
        "title": input_data.get("title") or template.get("title") or "Generated Document",
        "summary": summary,
        "content": content,
        "compliance_tags": input_data.get("compliance_tags") or template.get("constraints", {}).get("allowed_tags") or [],
        "sources": input_data.get("sources") or [],
        "approvals": approvals or [],
        "metadata": metadata,
    }


class FileSystemDocumentStore:
    def __init__(self, root: Path) -> None:
        self.root = root
        self.root.mkdir(parents=True, exist_ok=True)

    def save(self, document: Dict[str, Any], version: Optional[str] = None) -> Dict[str, Any]:
        doc_id = document.get("document_id")
        if not doc_id:
            raise ValueError("document_id required")
        version = version or _version_stamp()
        metadata = document.setdefault("metadata", {})
        metadata.setdefault("version", version)

        doc_dir = self.root / doc_id / "versions"
        doc_dir.mkdir(parents=True, exist_ok=True)
        path = doc_dir / f"{version}.json"
        path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")

        index_path = self.root / doc_id / "index.json"
        index = _load_index(index_path)
        if version not in index["versions"]:
            index["versions"].append(version)
            index["versions"].sort()
        index["latest_version"] = version
        index["updated_utc"] = _utc_now()
        index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")

        return {"document_id": doc_id, "version": version, "path": str(path)}

    def get(self, doc_id: str, version: Optional[str] = None) -> Dict[str, Any]:
        index = _load_index(self.root / doc_id / "index.json")
        if version is None:
            version = index.get("pinned_version") or index.get("latest_version")
        if not version:
            raise FileNotFoundError(f"no versions for {doc_id}")
        path = self.root / doc_id / "versions" / f"{version}.json"
        if not path.exists():
            raise FileNotFoundError(str(path))
        return json.loads(path.read_text(encoding="utf-8"))

    def list_versions(self, doc_id: str) -> List[str]:
        index = _load_index(self.root / doc_id / "index.json")
        return list(index.get("versions") or [])

    def rollback(self, doc_id: str, version: str) -> Dict[str, Any]:
        index_path = self.root / doc_id / "index.json"
        index = _load_index(index_path)
        if version not in index.get("versions", []):
            raise FileNotFoundError(f"version {version} not found for {doc_id}")
        index["pinned_version"] = version
        index["updated_utc"] = _utc_now()
        index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
        return {"document_id": doc_id, "pinned_version": version}


def _load_index(path: Path) -> Dict[str, Any]:
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    return {
        "schema_version": "v1",
        "versions": [],
        "latest_version": None,
        "pinned_version": None,
        "updated_utc": _utc_now(),
    }


def _version_stamp() -> str:
    return dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d%H%M%S")


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _summarize(mapped_fields: Dict[str, Any]) -> str:
    for key in ("purpose", "summary", "overview"):
        value = mapped_fields.get(key)
        if value:
            return str(value)[:200]
    if mapped_fields:
        first = next(iter(mapped_fields.values()))
        return str(first)[:200]
    return "Generated document."
