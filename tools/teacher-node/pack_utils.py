import hashlib
import json
from pathlib import Path
from typing import Any, Dict


def canonicalize_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def load_pack(pack_path: Path) -> Dict[str, Any]:
    return json.loads(pack_path.read_text(encoding="utf-8"))


def compute_anchor(pack_path: Path) -> str:
    data = load_pack(pack_path)
    canonical = canonicalize_json(data)
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return digest


def build_dependency_graph(pack_path: Path) -> Dict[str, Any]:
    data = load_pack(pack_path)
    dependencies = data.get("dependencies", {})
    packs = dependencies.get("packs", []) if isinstance(dependencies, dict) else []
    normalized = []
    for dep in packs:
        if isinstance(dep, str):
            normalized.append({"pack_id": dep})
            continue
        if isinstance(dep, dict):
            entry = {"pack_id": dep.get("pack_id")}
            version = dep.get("pack_version") or dep.get("version")
            if version:
                entry["pack_version"] = version
            if "optional" in dep:
                entry["optional"] = dep["optional"]
            normalized.append(entry)
    return {
        "pack_id": data.get("pack_id") or data.get("name"),
        "pack_version": data.get("pack_version"),
        "dependencies": normalized,
    }
