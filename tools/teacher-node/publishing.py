import datetime as dt
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from typing import Any, Dict, Optional

from artifact_store import ArtifactStore
from capability_map import generate_capability_map
from pack_utils import build_dependency_graph, canonicalize_json, compute_anchor, load_pack


def publish_pack(
    pack_dir: Path,
    out_dir: Path,
    version: Optional[str] = None,
    signing_key: Optional[str] = None,
    published_utc: Optional[str] = None,
    transport: str = "copy",
    dry_run: bool = False,
    artifact_store: Optional[ArtifactStore] = None,
    published_store: Optional[ArtifactStore] = None,
) -> Dict[str, Any]:
    pack_path = pack_dir / "pack.json"
    if not pack_path.exists():
        raise FileNotFoundError(str(pack_path))

    data = load_pack(pack_path)
    pack_id = data.get("pack_id") or data.get("name")
    if not pack_id:
        raise RuntimeError("pack_id missing")
    version = version or data.get("pack_version") or "0.0.0"

    anchor = compute_anchor(pack_path)
    graph = build_dependency_graph(pack_path)
    capability_map = generate_capability_map(data)

    signature = None
    if signing_key:
        signature = _sign_manifest(pack_id, version, anchor, signing_key)

    manifest = {
        "schema_version": "v1",
        "pack_id": pack_id,
        "pack_version": version,
        "anchor": anchor,
        "signature": signature,
        "published_utc": published_utc or _utc_now(),
        "dependency_graph": graph,
        "capability_map": {
            "path": "capability-map.json",
            "version": capability_map.get("schema_version", "v1"),
        },
    }

    published_layout = None
    if published_store is not None:
        published_layout = {
            "latest_key": f"{pack_id}/latest.txt",
            "pack_key": f"{pack_id}/{anchor}/pack.json",
            "anchor_key": f"{pack_id}/{anchor}/anchor.txt",
        }

    if transport == "copy":
        target = out_dir / pack_id / version
        if dry_run:
            result = {"target": str(target), "pack_id": pack_id, "version": version}
            if published_layout:
                result["published_layout"] = published_layout
            return result
        target.mkdir(parents=True, exist_ok=True)
        (target / "pack.json").write_text(_canonical_pack_text(data), encoding="utf-8")
        (target / "anchor.txt").write_text(anchor + "\n", encoding="utf-8")
        (target / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        (target / "dependency-graph.json").write_text(json.dumps(graph, indent=2) + "\n", encoding="utf-8")
        (target / "capability-map.json").write_text(
            json.dumps(capability_map, indent=2) + "\n", encoding="utf-8"
        )
        _update_registry_index_local(out_dir, pack_id, version)
    elif transport == "scp":
        if dry_run:
            result = {"target": str(out_dir), "pack_id": pack_id, "version": version}
            if published_layout:
                result["published_layout"] = published_layout
            return result
        _scp_publish(pack_dir, out_dir, pack_id, version)
    elif transport == "s3":
        if artifact_store is None:
            raise RuntimeError("artifact_store required for s3 transport")
        if dry_run:
            result = {"target": f"s3://{pack_id}/{version}", "pack_id": pack_id, "version": version}
            if published_layout:
                result["published_layout"] = published_layout
            return result
        prefix = f"packs/{pack_id}/{version}"
        with tempfile.TemporaryDirectory() as tmp:
            tmp_dir = Path(tmp)
            (tmp_dir / "pack.json").write_text(_canonical_pack_text(data), encoding="utf-8")
            (tmp_dir / "anchor.txt").write_text(anchor + "\n", encoding="utf-8")
            (tmp_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            (tmp_dir / "dependency-graph.json").write_text(json.dumps(graph, indent=2) + "\n", encoding="utf-8")
            (tmp_dir / "capability-map.json").write_text(
                json.dumps(capability_map, indent=2) + "\n", encoding="utf-8"
            )
            for path in tmp_dir.iterdir():
                artifact_store.upload(path, f"{prefix}/{path.name}")
        _update_registry_index_store(artifact_store, pack_id, version)
    else:
        raise RuntimeError(f"unsupported transport: {transport}")

    if published_store is not None:
        _publish_published_layout(published_store, pack_id, anchor, data)

    emit_provenance_event({
        "event_type": "pack.publish",
        "pack_id": pack_id,
        "pack_version": version,
        "anchor": anchor,
    })

    result = {
        "pack_id": pack_id,
        "pack_version": version,
        "anchor": anchor,
        "manifest": manifest,
    }
    if published_layout:
        result["published_layout"] = published_layout
    return result


def _publish_published_layout(
    store: ArtifactStore,
    pack_id: str,
    anchor: str,
    pack_data: Dict[str, Any],
) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        pack_path = tmp_dir / "pack.json"
        anchor_path = tmp_dir / "anchor.txt"
        latest_path = tmp_dir / "latest.txt"

        pack_path.write_text(_canonical_pack_text(pack_data), encoding="utf-8")
        anchor_path.write_text(anchor + "\n", encoding="utf-8")
        latest_path.write_text(anchor + "\n", encoding="utf-8")

        prefix = f"{pack_id}/{anchor}"
        store.upload(pack_path, f"{prefix}/pack.json")
        store.upload(anchor_path, f"{prefix}/anchor.txt")
        store.upload(latest_path, f"{pack_id}/latest.txt")


def _sign_manifest(pack_id: str, version: str, anchor: str, signing_key: str) -> str:
    payload = f"{pack_id}:{version}:{anchor}:{signing_key}".encode("utf-8")
    return _sha256(payload)


def _canonical_pack_text(pack_data: Dict[str, Any]) -> str:
    return canonicalize_json(pack_data)


def _sha256(payload: bytes) -> str:
    import hashlib

    digest = hashlib.sha256()
    digest.update(payload)
    return digest.hexdigest()


def _scp_publish(pack_dir: Path, out_dir: Path, pack_id: str, version: str) -> None:
    target = f"{out_dir}/{pack_id}/{version}"
    subprocess.check_call(["scp", "-r", str(pack_dir), target])


def _update_registry_index_local(root: Path, pack_id: str, version: str) -> None:
    index_path = root / "index.json"
    index = _load_registry_index(index_path)
    _apply_registry_update(index, pack_id, version)
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")


def _update_registry_index_store(store: ArtifactStore, pack_id: str, version: str) -> None:
    tmp = Path(tempfile.mkdtemp())
    index_path = tmp / "index.json"
    try:
        try:
            store.download("packs/index.json", index_path)
            index = json.loads(index_path.read_text(encoding="utf-8"))
        except Exception:
            index = _load_registry_index(None)
        _apply_registry_update(index, pack_id, version)
        index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
        store.upload(index_path, "packs/index.json")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def _load_registry_index(path: Optional[Path]) -> Dict[str, Any]:
    if path and path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    return {
        "schema_version": "v1",
        "generated_utc": _utc_now(),
        "packs": [],
    }


def _apply_registry_update(index: Dict[str, Any], pack_id: str, version: str) -> None:
    packs = index.setdefault("packs", [])
    entry = next((p for p in packs if p.get("pack_id") == pack_id), None)
    if entry is None:
        entry = {"pack_id": pack_id, "versions": []}
        packs.append(entry)
    if version not in entry["versions"]:
        entry["versions"].append(version)
        entry["versions"].sort()
    entry["latest_version"] = entry["versions"][-1]
    index["generated_utc"] = _utc_now()


def emit_provenance_event(event: Dict[str, Any]) -> None:
    path = Path(os.getenv("TEACHER_NODE_PROVENANCE_PATH", "artifacts/teacher-node/provenance-events.jsonl"))
    path.parent.mkdir(parents=True, exist_ok=True)
    event_record = {
        "schema_version": "v1",
        "recorded_utc": _utc_now(),
        **event,
    }
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(event_record) + "\n")

    endpoint = os.getenv("TEACHER_NODE_PROVENANCE_ENDPOINT")
    orchestrator = os.getenv("ORCHESTRATOR_URL")
    if endpoint and orchestrator:
        _post_provenance_event(orchestrator.rstrip("/"), endpoint, event_record)


def _post_provenance_event(base_url: str, endpoint: str, payload: Dict[str, Any]) -> None:
    import urllib.request

    data = json.dumps(payload).encode("utf-8")
    headers = {"Content-Type": "application/json"}
    token = os.getenv("TEACHER_NODE_PROVENANCE_TOKEN") or os.getenv("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(
        url=f"{base_url}{endpoint}",
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
