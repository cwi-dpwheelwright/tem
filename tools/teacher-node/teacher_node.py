#!/usr/bin/env python
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Optional

from artifact_registry import build_artifact_payload, maybe_register_artifact, resolve_tenant_id
from artifact_store import ArtifactStore, build_artifact_store_from_env
from capability_map import write_capability_map
from coherence import (
    build_escalation_request,
    check_coherence,
    maybe_post_escalation_request,
    write_coherence_report,
    write_escalation_request,
)
from documents import FileSystemDocumentStore, build_procedure_document
from governance import build_approval_request, evaluate_governance, maybe_post_approval_request
from pack_utils import build_dependency_graph, compute_anchor
from publishing import emit_provenance_event, publish_pack
from reconciliation import write_reconciliation_bundle
from reports import compute_sha256, render_narrative, write_report_bundle
from signing import sign_report_bundle
from simulations import run_simulations
from templates import (
    apply_semantic_mapping,
    load_template,
    validate_required_fields,
    validate_template,
)
from event_bus import publish_event
from teacher_critic import (
    run_teacher_critic,
    write_teacher_critic_report,
    write_validation_marker,
)


FEDERATED_LEARNING_SAFE_TOKEN = re.compile(r"^[a-z0-9_.:-]{1,64}$")
FEDERATED_LEARNING_UNSAFE_KEYS = {
    "raw",
    "raw_text",
    "text",
    "body",
    "prompt",
    "answer",
    "message",
    "conversation",
    "transcript",
    "file_content",
    "document",
    "email",
    "note",
    "observations",
}


def load_schema(schema_path: Path) -> dict:
    return json.loads(schema_path.read_text(encoding="utf-8"))


def validate_pack(pack_path: Path, schema_path: Path) -> None:
    try:
        import jsonschema  # type: ignore
    except ImportError as exc:
        raise RuntimeError("jsonschema is required for validation.") from exc

    schema = load_schema(schema_path)
    data = json.loads(pack_path.read_text(encoding="utf-8"))
    jsonschema.validate(instance=data, schema=schema)


def cmd_validate(args: argparse.Namespace) -> int:
    pack_path = Path(args.pack)
    schema_path = Path(args.schema)
    if not pack_path.exists():
        print(f"pack.json not found: {pack_path}", file=sys.stderr)
        return 1
    validate_pack(pack_path, schema_path)
    print("OK")
    return 0


def cmd_anchor(args: argparse.Namespace) -> int:
    pack_path = Path(args.pack)
    if not pack_path.exists():
        print(f"pack.json not found: {pack_path}", file=sys.stderr)
        return 1
    anchor = compute_anchor(pack_path)
    out_path = Path(args.out) if args.out else pack_path.with_name("anchor.txt")
    out_path.write_text(anchor + "\n", encoding="utf-8")
    print(anchor)
    return 0


def cmd_graph(args: argparse.Namespace) -> int:
    pack_path = Path(args.pack)
    if not pack_path.exists():
        print(f"pack.json not found: {pack_path}", file=sys.stderr)
        return 1
    graph = build_dependency_graph(pack_path)
    out_path = Path(args.out) if args.out else pack_path.with_name("dependency-graph.json")
    out_path.write_text(json.dumps(graph, indent=2) + "\n", encoding="utf-8")
    print(out_path)
    return 0


def cmd_capability_map(args: argparse.Namespace) -> int:
    pack_path = Path(args.pack)
    if not pack_path.exists():
        print(f"pack.json not found: {pack_path}", file=sys.stderr)
        return 1
    out_path = Path(args.out) if args.out else pack_path.with_name("capability-map.json")
    write_capability_map(pack_path, out_path)
    print(out_path)
    return 0


def cmd_publish(args: argparse.Namespace) -> int:
    pack_dir = Path(args.pack_dir)
    out_dir = Path(args.out_dir)
    transport = args.transport
    dry_run = args.dry_run

    artifact_store = None
    if transport == "s3":
        backend = os.getenv("TEACHER_NODE_ARTIFACT_BACKEND", "filesystem").lower()
        if backend != "minio":
            raise RuntimeError("TEACHER_NODE_ARTIFACT_BACKEND=minio required for s3 transport")
        artifact_store = build_artifact_store_from_env()

    published_store = None
    if args.published_layout:
        published_bucket = os.getenv("TEACHER_NODE_PUBLISHED_BUCKET", "cwi-packs-published")
        published_root = os.getenv("TEACHER_NODE_PUBLISHED_ROOT")
        root_override = Path(published_root) if published_root else None
        published_store = build_artifact_store_from_env(
            bucket_override=published_bucket,
            root_override=root_override,
        )

    result = publish_pack(
        pack_dir=pack_dir,
        out_dir=out_dir,
        version=args.version,
        signing_key=args.signing_key,
        published_utc=args.published_utc,
        transport=transport,
        dry_run=dry_run,
        artifact_store=artifact_store,
        published_store=published_store,
    )

    if args.report_bundle:
        publish_report_bundle(
            bundle_path=Path(args.report_bundle),
            pack_id=result["pack_id"],
            pack_version=result["pack_version"],
            run_id=args.run_id,
            artifact_store=artifact_store,
            out_dir=out_dir,
        )

    print(json.dumps(result, indent=2))
    return 0


def cmd_rollback(args: argparse.Namespace) -> int:
    registry_dir = Path(args.registry_dir)
    pins_dir = registry_dir / "pins"
    pins_dir.mkdir(parents=True, exist_ok=True)
    pin_path = pins_dir / f"{args.pack_id}.json"
    pin = {"pack_id": args.pack_id, "pinned_version": args.version}
    pin_path.write_text(json.dumps(pin, indent=2) + "\n", encoding="utf-8")

    rollback_event = {
        "pack_id": args.pack_id,
        "version": args.version,
        "reason": args.reason or "rollback",
    }
    (pins_dir / f"{args.pack_id}-rollback.json").write_text(
        json.dumps(rollback_event, indent=2) + "\n", encoding="utf-8"
    )
    print(pin_path)
    return 0


def cmd_eval_setup(args: argparse.Namespace) -> int:
    vector_fixture = args.vector_fixture
    if vector_fixture:
        ingest_script = Path(
            "src/backend/services/cwi-orchestrator/scripts/ingest-vector-fixtures.py"
        )
        if not ingest_script.exists():
            print("ingest-vector-fixtures.py not found", file=sys.stderr)
            return 1
        cmd = [
            sys.executable,
            str(ingest_script),
            "--input",
            vector_fixture,
        ]
        if args.collection:
            cmd.extend(["--collection", args.collection])
        if args.dimension:
            cmd.extend(["--dimension", str(args.dimension)])
        if args.shard_url:
            for shard in args.shard_url:
                cmd.extend(["--shard-url", shard])
        subprocess.check_call(cmd)
    print("OK")
    return 0


def cmd_report_generate(args: argparse.Namespace) -> int:
    input_path = Path(args.input)
    if not input_path.exists():
        print(f"report input not found: {input_path}", file=sys.stderr)
        return 1
    input_data = json.loads(input_path.read_text(encoding="utf-8"))

    out_dir = Path(args.out_dir) if args.out_dir else Path("artifacts/teacher-node/reports")
    report_dir = out_dir / (input_data.get("pack_id") or "unknown") / (input_data.get("run_id") or "run")

    paths = write_report_bundle(input_data, report_dir)
    structured = json.loads(paths["report_json"].read_text(encoding="utf-8"))
    narrative = paths["report_md"].read_text(encoding="utf-8")

    coherence_report = check_coherence(structured, narrative)
    coherence_path = report_dir / "coherence.json"
    write_coherence_report(coherence_report, coherence_path)

    if coherence_report["status"] != "pass" and args.escalate_on_conflict:
        request = build_escalation_request(structured, coherence_report, "coherence_conflict")
        request_path = report_dir / "approval-request.json"
        write_escalation_request(request, request_path)
        maybe_post_escalation_request(request)

    signature_path = None
    if args.signing_key:
        signature = sign_report_bundle(
            paths["report_json"],
            paths["report_md"],
            Path(args.signing_key),
        )
        signature_path = report_dir / "signature.json"
        signature_path.write_text(json.dumps(signature, indent=2) + "\n", encoding="utf-8")

    bundle_path = None
    if args.bundle:
        bundle_path = report_dir / "report-bundle.zip"
        create_export_bundle(report_dir, bundle_path)

    emit_provenance_event({
        "event_type": "report.generate",
        "pack_id": structured.get("pack_id"),
        "pack_version": structured.get("pack_version"),
        "run_id": structured.get("run_id"),
    })

    tenant_id = resolve_tenant_id(input_data)
    if tenant_id:
        artifact_id = structured.get("report_id") or structured.get("run_id") or "report"
        version = structured.get("run_id") or structured.get("pack_version") or "v1"
        payload = build_artifact_payload(
            tenant_id=tenant_id,
            artifact_id=str(artifact_id),
            artifact_type="report",
            version=str(version),
            artifact_hash=compute_sha256(paths["report_json"]),
            title="Teacher Node Evaluation Report",
            provenance_refs=[
                f"report.generate:{structured.get('run_id') or ''}".rstrip(":")
            ],
            metadata={
                "pack_id": str(structured.get("pack_id") or ""),
                "pack_version": str(structured.get("pack_version") or ""),
                "run_id": str(structured.get("run_id") or ""),
            },
        )
        maybe_register_artifact(payload)

    result = {
        "report_dir": str(report_dir),
        "report_json": str(paths["report_json"]),
        "report_md": str(paths["report_md"]),
        "coherence": str(coherence_path),
    }
    if signature_path:
        result["signature"] = str(signature_path)
    if bundle_path:
        result["bundle"] = str(bundle_path)

    print(json.dumps(result, indent=2))
    return 0


def cmd_template_validate(args: argparse.Namespace) -> int:
    template_path = Path(args.template)
    schema_path = Path(args.schema)
    if not template_path.exists():
        print(f"template not found: {template_path}", file=sys.stderr)
        return 1
    template = load_template(template_path)
    validate_template(template, schema_path)
    print("OK")
    return 0


def cmd_document_generate(args: argparse.Namespace) -> int:
    template_path = Path(args.template)
    input_path = Path(args.input)
    if not template_path.exists():
        print(f"template not found: {template_path}", file=sys.stderr)
        return 1
    if not input_path.exists():
        print(f"document input not found: {input_path}", file=sys.stderr)
        return 1

    template = load_template(template_path)
    if args.schema:
        validate_template(template, Path(args.schema))

    input_data = json.loads(input_path.read_text(encoding="utf-8"))
    rules = None
    if args.mapping:
        rules = json.loads(Path(args.mapping).read_text(encoding="utf-8"))

    mapped = apply_semantic_mapping(template, input_data, rules)
    missing = validate_required_fields(template, mapped)
    if missing:
        print(f"missing required fields: {', '.join(missing)}", file=sys.stderr)
        return 1

    governance = evaluate_governance(template, input_data)
    document = build_procedure_document(
        template=template,
        mapped_fields=mapped,
        input_data=input_data,
        approvals=governance["approvals"],
    )

    version = args.version or _doc_version_stamp()
    store_result = None
    if args.store_dir:
        store = FileSystemDocumentStore(Path(args.store_dir))
        store_result = store.save(document, version=version)
        version = store_result["version"]
        document.setdefault("metadata", {})["version"] = version
    else:
        document.setdefault("metadata", {})["version"] = version

    out_dir = Path(args.out_dir) if args.out_dir else Path("artifacts/teacher-node/documents")
    doc_dir = out_dir / document["document_id"] / version
    doc_dir.mkdir(parents=True, exist_ok=True)
    doc_json = doc_dir / "document.json"
    doc_md = doc_dir / "document.md"
    doc_json.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    doc_md.write_text(document.get("content", ""), encoding="utf-8")

    approval_path = None
    if governance["requires_approval"] and args.request_approval:
        request = build_approval_request(document, template, governance["approval_reason"])
        approval_path = doc_dir / "approval-request.json"
        approval_path.write_text(json.dumps(request, indent=2) + "\n", encoding="utf-8")
        maybe_post_approval_request(request)

    bundle_path = None
    if args.bundle:
        bundle_path = doc_dir / "document-bundle.zip"
        create_export_bundle(doc_dir, bundle_path)

    emit_provenance_event({
        "event_type": "document.generate",
        "document_id": document.get("document_id"),
        "template_id": template.get("template_id"),
        "template_version": template.get("version"),
        "version": version,
    })

    tenant_id = resolve_tenant_id(document)
    if tenant_id:
        payload = build_artifact_payload(
            tenant_id=tenant_id,
            artifact_id=str(document.get("document_id") or ""),
            artifact_type="document",
            version=str(version),
            artifact_hash=compute_sha256(doc_json),
            title=str(document.get("title") or "Document"),
            provenance_refs=[f"document.generate:{version}"],
            metadata={
                "template_id": str(template.get("template_id") or ""),
                "template_version": str(template.get("version") or ""),
            },
        )
        maybe_register_artifact(payload)

    result = {
        "document_dir": str(doc_dir),
        "document_json": str(doc_json),
        "document_md": str(doc_md),
    }
    if approval_path:
        result["approval_request"] = str(approval_path)
    if bundle_path:
        result["bundle"] = str(bundle_path)
    if store_result:
        result["store_path"] = store_result["path"]

    print(json.dumps(result, indent=2))
    return 0


def cmd_document_rollback(args: argparse.Namespace) -> int:
    store = FileSystemDocumentStore(Path(args.store_dir))
    result = store.rollback(args.document_id, args.version)
    print(json.dumps(result, indent=2))
    return 0


def cmd_reconciliation_generate(args: argparse.Namespace) -> int:
    input_path = Path(args.input)
    if not input_path.exists():
        print(f"reconciliation input not found: {input_path}", file=sys.stderr)
        return 1
    input_data = json.loads(input_path.read_text(encoding="utf-8"))
    out_dir = Path(args.out_dir) if args.out_dir else Path("artifacts/teacher-node/reconciliation")
    tenant_id = input_data.get("tenant_id") or "unknown"
    run_id = input_data.get("run_id") or "run"
    report_dir = out_dir / tenant_id / run_id
    paths = write_reconciliation_bundle(input_data, report_dir)

    bundle_path = None
    if args.bundle:
        bundle_path = report_dir / "reconciliation-bundle.zip"
        create_export_bundle(report_dir, bundle_path)

    if args.publish:
        artifact_store = build_artifact_store_from_env()
        if bundle_path is None:
            bundle_path = report_dir / "reconciliation-bundle.zip"
            create_export_bundle(report_dir, bundle_path)
        publish_reconciliation_bundle(
            bundle_path=bundle_path,
            tenant_id=tenant_id,
            run_id=run_id,
            artifact_store=artifact_store,
            out_dir=out_dir,
        )

    emit_provenance_event({
        "event_type": "reconciliation.report.generate",
        "tenant_id": tenant_id,
        "run_id": run_id,
    })

    report_data = json.loads(paths["report_json"].read_text(encoding="utf-8"))
    if tenant_id:
        payload = build_artifact_payload(
            tenant_id=tenant_id,
            artifact_id=str(report_data.get("report_id") or run_id),
            artifact_type="reconciliation_report",
            version=str(report_data.get("run_id") or run_id),
            artifact_hash=compute_sha256(paths["report_json"]),
            title=str(report_data.get("title") or "Reconciliation Report"),
            provenance_refs=[f"reconciliation.report.generate:{run_id}"],
            metadata={
                "run_id": str(report_data.get("run_id") or run_id),
                "status": str(report_data.get("status") or ""),
            },
        )
        maybe_register_artifact(payload)

    result = {
        "report_dir": str(report_dir),
        "report_json": str(paths["report_json"]),
        "report_md": str(paths["report_md"]),
        "manifest": str(paths["manifest"]),
    }
    if paths.get("discrepancies"):
        result["discrepancies"] = str(paths["discrepancies"])
    if bundle_path:
        result["bundle"] = str(bundle_path)

    print(json.dumps(result, indent=2))
    return 0


def cmd_simulation_run(args: argparse.Namespace) -> int:
    results = run_simulations()
    payload = [result.__dict__ for result in results]
    if args.out:
        out_path = Path(args.out)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        print(out_path)
        return 0
    print(json.dumps(payload, indent=2))
    return 0


def cmd_critic_run(args: argparse.Namespace) -> int:
    pack_roots = tuple(Path(path) for path in args.pack_root or [])
    artifact_root = Path(args.artifact_root) if args.artifact_root else _default_artifact_root()
    vector_root = Path(args.vector_root) if args.vector_root else _default_vector_root()
    report = run_teacher_critic(
        pack_roots=pack_roots,
        artifact_root=artifact_root,
        vector_root=vector_root,
    )

    if args.out:
        write_teacher_critic_report(report, Path(args.out))
    if args.validation_marker:
        write_validation_marker(report, Path(args.validation_marker))

    print(json.dumps(report, indent=2))
    return 1 if report["status"] == "fail" else 0


def create_export_bundle(report_dir: Path, bundle_path: Path) -> None:
    bundle_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(bundle_path, "w", zipfile.ZIP_DEFLATED) as bundle:
        for path in report_dir.iterdir():
            if path.is_file():
                bundle.write(path, arcname=path.name)


def publish_report_bundle(
    bundle_path: Path,
    pack_id: str,
    pack_version: str,
    run_id: Optional[str],
    artifact_store: Optional[ArtifactStore],
    out_dir: Path,
) -> None:
    run_id = run_id or "run"
    if artifact_store is None:
        target_dir = out_dir / "reports" / pack_id / pack_version / run_id
        target_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(bundle_path, target_dir / bundle_path.name)
        _update_report_index_local(out_dir, pack_id, pack_version, run_id, target_dir / bundle_path.name)
        return

    key = f"reports/{pack_id}/{pack_version}/{run_id}/{bundle_path.name}"
    artifact_store.upload(bundle_path, key)
    _update_report_index_store(artifact_store, pack_id, pack_version, run_id, key)


def publish_reconciliation_bundle(
    bundle_path: Path,
    tenant_id: str,
    run_id: str,
    artifact_store: Optional[ArtifactStore],
    out_dir: Path,
) -> None:
    if artifact_store is None:
        target_dir = out_dir / "reconciliation" / tenant_id / run_id
        target_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(bundle_path, target_dir / bundle_path.name)
        _update_reconciliation_index_local(out_dir, tenant_id, run_id, target_dir / bundle_path.name)
        return

    key = f"reconciliation/{tenant_id}/{run_id}/{bundle_path.name}"
    artifact_store.upload(bundle_path, key)
    _update_reconciliation_index_store(artifact_store, tenant_id, run_id, key)


def _update_report_index_local(root: Path, pack_id: str, pack_version: str, run_id: str, bundle_path: Path) -> None:
    index_path = root / "reports" / "index.json"
    index = _load_report_index(index_path)
    _apply_report_index(index, pack_id, pack_version, run_id, str(bundle_path))
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")


def _update_report_index_store(store: ArtifactStore, pack_id: str, pack_version: str, run_id: str, key: str) -> None:
    tmp = Path(tempfile.mkdtemp())
    index_path = tmp / "index.json"
    try:
        try:
            store.download("reports/index.json", index_path)
            index = json.loads(index_path.read_text(encoding="utf-8"))
        except Exception:
            index = _load_report_index(None)
        _apply_report_index(index, pack_id, pack_version, run_id, key)
        index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
        store.upload(index_path, "reports/index.json")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def _update_reconciliation_index_local(root: Path, tenant_id: str, run_id: str, bundle_path: Path) -> None:
    index_path = root / "reconciliation" / "index.json"
    index = _load_reconciliation_index(index_path)
    _apply_reconciliation_index(index, tenant_id, run_id, str(bundle_path))
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")


def _update_reconciliation_index_store(store: ArtifactStore, tenant_id: str, run_id: str, key: str) -> None:
    tmp = Path(tempfile.mkdtemp())
    index_path = tmp / "index.json"
    try:
        try:
            store.download("reconciliation/index.json", index_path)
            index = json.loads(index_path.read_text(encoding="utf-8"))
        except Exception:
            index = _load_reconciliation_index(None)
        _apply_reconciliation_index(index, tenant_id, run_id, key)
        index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
        store.upload(index_path, "reconciliation/index.json")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def _load_report_index(path: Path | None) -> dict:
    if path and path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    return {
        "schema_version": "v1",
        "generated_utc": "",
        "reports": [],
    }


def _load_reconciliation_index(path: Path | None) -> dict:
    if path and path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    return {
        "schema_version": "v1",
        "generated_utc": "",
        "runs": [],
    }


def _apply_report_index(index: dict, pack_id: str, pack_version: str, run_id: str, bundle_key: str) -> None:
    reports = index.setdefault("reports", [])
    entry = next((r for r in reports if r.get("pack_id") == pack_id and r.get("pack_version") == pack_version), None)
    if entry is None:
        entry = {"pack_id": pack_id, "pack_version": pack_version, "runs": []}
        reports.append(entry)
    entry["runs"].append({"run_id": run_id, "bundle": bundle_key})
    index["generated_utc"] = dt_utc_now()


def _apply_reconciliation_index(index: dict, tenant_id: str, run_id: str, bundle_key: str) -> None:
    runs = index.setdefault("runs", [])
    runs.append({"tenant_id": tenant_id, "run_id": run_id, "bundle": bundle_key})
    index["generated_utc"] = dt_utc_now()


def dt_utc_now() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _doc_version_stamp() -> str:
    from datetime import datetime, timezone

    return datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S")


def _federated_learning_root(root_override: Optional[Path] = None) -> Path:
    if root_override is not None:
        return root_override
    configured = os.getenv("TEACHER_NODE_FEDERATED_LEARNING_ROOT", "").strip()
    return Path(configured) if configured else Path("artifacts/teacher-node/federated-learning")


def _default_artifact_root() -> Optional[Path]:
    configured = os.getenv("TEACHER_NODE_ARTIFACT_ROOT", "").strip()
    return Path(configured) if configured else None


def _default_vector_root() -> Optional[Path]:
    configured = (
        os.getenv("TEACHER_NODE_VECTOR_ROOT", "").strip()
        or os.getenv("TEACHER_NODE_QDRANT_EXPORT_ROOT", "").strip()
    )
    return Path(configured) if configured else None


def _contains_unsafe_learning_key(value) -> bool:
    if isinstance(value, dict):
        for key, nested in value.items():
            normalized = str(key).strip().lower()
            if normalized in FEDERATED_LEARNING_UNSAFE_KEYS:
                if normalized == "raw_content_exported" and nested is False:
                    continue
                return True
            if _contains_unsafe_learning_key(nested):
                return True
    elif isinstance(value, list):
        return any(_contains_unsafe_learning_key(item) for item in value)
    return False


def _validate_federated_learning_group(group: dict) -> tuple[bool, str]:
    for key in ("category", "signal", "outcome"):
        token = str(group.get(key, "")).strip()
        if not FEDERATED_LEARNING_SAFE_TOKEN.fullmatch(token):
            return False, f"unsafe_{key}"
    try:
        count = int(group.get("count", 0))
    except (TypeError, ValueError):
        return False, "invalid_count"
    if count <= 0:
        return False, "invalid_count"
    for tag in group.get("tags", []) or []:
        if not FEDERATED_LEARNING_SAFE_TOKEN.fullmatch(str(tag).strip()):
            return False, "unsafe_tag"
    return True, "ok"


def record_federated_learning_summary(payload: dict, root_override: Optional[Path] = None) -> tuple[int, dict]:
    required = ("schema_version", "summary_id", "tenant_id", "trace_id", "policy_status", "summary_groups", "provenance")
    missing = [field for field in required if field not in payload]
    if missing:
        return 400, {"status": "rejected", "reason": "missing_required_fields", "missing": missing}

    if payload.get("raw_content_exported") is not False:
        return 400, {"status": "rejected", "reason": "raw_content_export_flag_not_false"}

    if _contains_unsafe_learning_key(payload):
        return 400, {"status": "rejected", "reason": "unsafe_raw_content_field"}

    groups = payload.get("summary_groups")
    if not isinstance(groups, list) or not groups:
        return 400, {"status": "rejected", "reason": "summary_groups_required"}

    for group in groups:
        if not isinstance(group, dict):
            return 400, {"status": "rejected", "reason": "invalid_summary_group"}
        valid, reason = _validate_federated_learning_group(group)
        if not valid:
            return 400, {"status": "rejected", "reason": reason}

    root = _federated_learning_root(root_override)
    root.mkdir(parents=True, exist_ok=True)
    summaries_path = root / "summaries.jsonl"
    aggregate_path = root / "aggregate.json"

    stored_summary = {
        "schema_version": payload["schema_version"],
        "summary_id": payload["summary_id"],
        "tenant_id": payload["tenant_id"],
        "user_id_hash": payload.get("user_id_hash"),
        "node_id": payload.get("node_id"),
        "trace_id": payload["trace_id"],
        "policy_status": payload["policy_status"],
        "source_component": payload.get("source_component"),
        "generated_utc": payload.get("generated_utc") or dt_utc_now(),
        "raw_content_exported": False,
        "summary_groups": groups,
        "provenance": payload["provenance"],
        "recorded_utc": dt_utc_now(),
    }
    with summaries_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(stored_summary, sort_keys=True) + "\n")

    aggregate = {
        "summary_count": 0,
        "observation_count": 0,
        "category_counts": {},
        "signal_counts": {},
        "last_summary_id": None,
        "updated_utc": None,
    }
    if aggregate_path.exists():
        aggregate = json.loads(aggregate_path.read_text(encoding="utf-8"))

    aggregate["summary_count"] = int(aggregate.get("summary_count", 0)) + 1
    aggregate["last_summary_id"] = payload["summary_id"]
    aggregate["updated_utc"] = dt_utc_now()
    for group in groups:
        count = int(group["count"])
        aggregate["observation_count"] = int(aggregate.get("observation_count", 0)) + count
        category = group["category"]
        signal = group["signal"]
        aggregate["category_counts"][category] = int(aggregate["category_counts"].get(category, 0)) + count
        aggregate["signal_counts"][signal] = int(aggregate["signal_counts"].get(signal, 0)) + count

    aggregate_path.write_text(json.dumps(aggregate, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 202, {
        "status": "accepted",
        "summary_id": payload["summary_id"],
        "aggregate": aggregate,
    }


def build_teacher_health_payload() -> tuple[int, dict]:
    checks: dict[str, dict] = {
        "teacher_app": {
            "status": "healthy",
            "detail": "Teacher node HTTP process is running.",
        }
    }

    checks["artifact_store"] = _check_artifact_store()
    checks["graph_staging"] = _check_graph_staging()
    checks["last_successful_validation"] = _check_last_successful_validation()

    failed = any(check["status"] in {"degraded", "failure"} for check in checks.values())
    status = "degraded" if failed else "healthy"
    return 503 if failed else 200, {
        "status": status,
        "service": "teacher-node",
        "checked_utc": dt_utc_now(),
        "checks": checks,
        "last_successful_validation_utc": checks["last_successful_validation"].get("timestamp_utc"),
    }


def _check_artifact_store() -> dict:
    try:
        store = build_artifact_store_from_env()
        # Listing one item proves credentials/path/bucket are reachable without
        # copying artifacts or mutating Teacher-owned state.
        next(iter(store.list("")), None)
        return {
            "status": "healthy",
            "detail": "Artifact store is reachable.",
            "evidence": "artifact_store.list",
        }
    except Exception as exc:  # pragma: no cover - exact backend failures vary
        return {
            "status": "degraded",
            "detail": f"Artifact store check failed: {type(exc).__name__}",
            "evidence": "artifact_store.list",
        }


def _check_graph_staging() -> dict:
    configured_path = os.getenv("TEACHER_NODE_GRAPH_STAGING_ROOT", "").strip()
    if not configured_path:
        return {
            "status": "unknown",
            "detail": "Graph staging root is not configured.",
            "evidence": "TEACHER_NODE_GRAPH_STAGING_ROOT",
        }

    path = Path(configured_path)
    if not path.exists():
        return {
            "status": "degraded",
            "detail": "Graph staging root does not exist.",
            "evidence": "TEACHER_NODE_GRAPH_STAGING_ROOT",
        }

    if not path.is_dir():
        return {
            "status": "degraded",
            "detail": "Graph staging root is not a directory.",
            "evidence": "TEACHER_NODE_GRAPH_STAGING_ROOT",
        }

    return {
        "status": "healthy",
        "detail": "Graph staging root is available.",
        "evidence": "TEACHER_NODE_GRAPH_STAGING_ROOT",
    }


def _check_last_successful_validation() -> dict:
    configured_path = os.getenv("TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH", "").strip()
    if not configured_path:
        return {
            "status": "unknown",
            "detail": "No validation marker path is configured.",
            "evidence": "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH",
        }

    path = Path(configured_path)
    if not path.exists() or not path.is_file():
        return {
            "status": "unknown",
            "detail": "Validation marker has not been written yet.",
            "evidence": "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH",
        }

    try:
        raw = path.read_text(encoding="utf-8").strip()
        timestamp = raw
        if raw.startswith("{"):
            payload = json.loads(raw)
            timestamp = (
                payload.get("timestamp_utc")
                or payload.get("validated_utc")
                or payload.get("completed_utc")
                or ""
            )
        return {
            "status": "healthy" if timestamp else "unknown",
            "timestamp_utc": timestamp or None,
            "detail": "Validation marker was found." if timestamp else "Validation marker did not include a timestamp.",
            "evidence": "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH",
        }
    except Exception as exc:
        return {
            "status": "unknown",
            "detail": f"Validation marker could not be read: {type(exc).__name__}",
            "evidence": "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH",
        }


def cmd_artifact_upload(args: argparse.Namespace) -> int:
    store = build_artifact_store_from_env()
    store.upload(Path(args.source), args.key)
    print(args.key)
    return 0


def cmd_artifact_download(args: argparse.Namespace) -> int:
    store = build_artifact_store_from_env()
    store.download(args.key, Path(args.dest))
    print(args.dest)
    return 0


def cmd_artifact_prune(args: argparse.Namespace) -> int:
    store = build_artifact_store_from_env()
    deleted = store.prune(args.prefix, args.older_than_days)
    print(deleted)
    return 0


def cmd_serve(args: argparse.Namespace) -> int:
    host = args.host
    port = args.port

    class Handler(BaseHTTPRequestHandler):
        def _write_json(self, status_code: int, payload: dict) -> None:
            body = (json.dumps(payload) + "\n").encode("utf-8")
            self.send_response(status_code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:  # noqa: N802
            if self.path == "/health":
                status_code, payload = build_teacher_health_payload()
                self._write_json(status_code, payload)
                return
            self._write_json(404, {"error": "not_found"})

        def do_POST(self) -> None:  # noqa: N802
            path = self.path.split("?", 1)[0]
            if path == "/federated-learning/summaries":
                length = int(self.headers.get("Content-Length", "0"))
                if length <= 0:
                    self._write_json(400, {"status": "rejected", "reason": "empty_body"})
                    return
                if length > 1024 * 1024:
                    self._write_json(413, {"status": "rejected", "reason": "payload_too_large"})
                    return
                try:
                    payload = json.loads(self.rfile.read(length).decode("utf-8"))
                except json.JSONDecodeError:
                    self._write_json(400, {"status": "rejected", "reason": "invalid_json"})
                    return
                status_code, response = record_federated_learning_summary(payload)
                self._write_json(status_code, response)
                return
            self._write_json(404, {"error": "not_found"})

        def log_message(self, format: str, *args) -> None:
            return

    server = ThreadingHTTPServer((host, port), Handler)
    print(f"teacher-node listening on {host}:{port}")
    publish_event(
        "service.start",
        "positive",
        "info",
        f"teacher-node started on {host}:{port}",
    )
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        publish_event(
            "service.stop",
            "negative",
            "warn",
            "teacher-node stopping (KeyboardInterrupt)",
        )
    except Exception as exc:
        publish_event(
            "service.error",
            "negative",
            "error",
            str(exc),
            data={"stack": repr(exc)},
        )
        raise
    finally:
        publish_event(
            "service.stop",
            "negative",
            "warn",
            "teacher-node stopping",
        )
        server.server_close()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Teacher node toolchain CLI")
    sub = parser.add_subparsers(dest="command", required=True)

    validate = sub.add_parser("validate")
    validate.add_argument("--pack", required=True)
    validate.add_argument(
        "--schema", default="contracts/json-schema/pack_schema.json"
    )
    validate.set_defaults(func=cmd_validate)

    anchor = sub.add_parser("anchor")
    anchor.add_argument("--pack", required=True)
    anchor.add_argument("--out")
    anchor.set_defaults(func=cmd_anchor)

    graph = sub.add_parser("graph")
    graph.add_argument("--pack", required=True)
    graph.add_argument("--out")
    graph.set_defaults(func=cmd_graph)

    capmap = sub.add_parser("capability-map")
    capmap.add_argument("--pack", required=True)
    capmap.add_argument("--out")
    capmap.set_defaults(func=cmd_capability_map)

    publish = sub.add_parser("publish")
    publish.add_argument("--pack-dir", required=True)
    publish.add_argument("--out-dir", required=True)
    publish.add_argument("--version")
    publish.add_argument("--signing-key")
    publish.add_argument("--published-utc")
    publish.add_argument("--transport", choices=["copy", "s3", "scp"], default="copy")
    publish.add_argument("--dry-run", action="store_true")
    publish.add_argument(
        "--published-layout",
        action="store_true",
        help="Write content-addressed publish layout (latest.txt + <hash>/pack.json) to artifact store",
    )
    publish.add_argument("--report-bundle")
    publish.add_argument("--run-id")
    publish.set_defaults(func=cmd_publish)

    rollback = sub.add_parser("rollback")
    rollback.add_argument("--registry-dir", required=True)
    rollback.add_argument("--pack-id", required=True)
    rollback.add_argument("--version", required=True)
    rollback.add_argument("--reason")
    rollback.set_defaults(func=cmd_rollback)

    eval_setup = sub.add_parser("eval-setup")
    eval_setup.add_argument("--vector-fixture")
    eval_setup.add_argument("--collection")
    eval_setup.add_argument("--dimension", type=int)
    eval_setup.add_argument("--shard-url", action="append")
    eval_setup.set_defaults(func=cmd_eval_setup)

    report = sub.add_parser("report-generate")
    report.add_argument("--input", required=True)
    report.add_argument("--out-dir")
    report.add_argument("--signing-key")
    report.add_argument("--bundle", action="store_true")
    report.add_argument("--escalate-on-conflict", action="store_true")
    report.set_defaults(func=cmd_report_generate)

    template_validate = sub.add_parser("template-validate")
    template_validate.add_argument("--template", required=True)
    template_validate.add_argument(
        "--schema", default="contracts/json-schema/teacher_template_schema.json"
    )
    template_validate.set_defaults(func=cmd_template_validate)

    document_generate = sub.add_parser("document-generate")
    document_generate.add_argument("--template", required=True)
    document_generate.add_argument("--input", required=True)
    document_generate.add_argument(
        "--schema", default="contracts/json-schema/teacher_template_schema.json"
    )
    document_generate.add_argument("--mapping")
    document_generate.add_argument("--out-dir")
    document_generate.add_argument("--store-dir")
    document_generate.add_argument("--version")
    document_generate.add_argument("--bundle", action="store_true")
    document_generate.add_argument("--request-approval", action="store_true")
    document_generate.set_defaults(func=cmd_document_generate)

    document_rollback = sub.add_parser("document-rollback")
    document_rollback.add_argument("--store-dir", required=True)
    document_rollback.add_argument("--document-id", required=True)
    document_rollback.add_argument("--version", required=True)
    document_rollback.set_defaults(func=cmd_document_rollback)

    reconciliation_generate = sub.add_parser("reconciliation-generate")
    reconciliation_generate.add_argument("--input", required=True)
    reconciliation_generate.add_argument("--out-dir")
    reconciliation_generate.add_argument("--bundle", action="store_true")
    reconciliation_generate.add_argument("--publish", action="store_true")
    reconciliation_generate.set_defaults(func=cmd_reconciliation_generate)

    simulation_run = sub.add_parser("simulation-run")
    simulation_run.add_argument("--out")
    simulation_run.set_defaults(func=cmd_simulation_run)

    critic_run = sub.add_parser("critic-run")
    critic_run.add_argument("--pack-root", action="append")
    critic_run.add_argument("--artifact-root")
    critic_run.add_argument("--vector-root")
    critic_run.add_argument("--out")
    critic_run.add_argument("--validation-marker")
    critic_run.set_defaults(func=cmd_critic_run)

    artifact_upload = sub.add_parser("artifact-upload")
    artifact_upload.add_argument("--source", required=True)
    artifact_upload.add_argument("--key", required=True)
    artifact_upload.set_defaults(func=cmd_artifact_upload)

    artifact_download = sub.add_parser("artifact-download")
    artifact_download.add_argument("--key", required=True)
    artifact_download.add_argument("--dest", required=True)
    artifact_download.set_defaults(func=cmd_artifact_download)

    artifact_prune = sub.add_parser("artifact-prune")
    artifact_prune.add_argument("--prefix", default="")
    artifact_prune.add_argument("--older-than-days", type=int, required=True)
    artifact_prune.set_defaults(func=cmd_artifact_prune)

    serve = sub.add_parser("serve")
    serve.add_argument("--host", default="0.0.0.0")
    serve.add_argument("--port", type=int, default=int(os.getenv("TEACHER_NODE_HTTP_PORT", "8084")))
    serve.set_defaults(func=cmd_serve)

    args = parser.parse_args()
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
