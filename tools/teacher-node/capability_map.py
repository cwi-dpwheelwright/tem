import datetime as dt
import json
from pathlib import Path
from typing import Any, Dict, List


def load_pack(pack_path: Path) -> Dict[str, Any]:
    return json.loads(pack_path.read_text(encoding="utf-8"))


def generate_capability_map(pack: Dict[str, Any]) -> Dict[str, Any]:
    pack_id = pack.get("pack_id") or pack.get("name") or "unknown"
    version = pack.get("pack_version") or "0.0.0"
    raw_caps = pack.get("capabilities")
    if raw_caps is None:
        raw_caps = pack.get("metadata", {}).get("capabilities", [])

    capabilities: List[Dict[str, Any]] = []
    for entry in raw_caps or []:
        if isinstance(entry, str):
            capabilities.append({
                "capability": entry,
                "actions": [],
                "permissions": [],
                "limits": {},
            })
            continue
        if isinstance(entry, dict):
            capabilities.append({
                "capability": entry.get("capability") or entry.get("name") or "unknown",
                "actions": list(entry.get("actions", []) or []),
                "permissions": list(entry.get("permissions", []) or []),
                "limits": entry.get("limits", {}) or {},
            })

    activation_map: List[Dict[str, Any]] = []
    for cap in capabilities:
        for action in cap.get("actions", []) or []:
            activation_map.append({
                "action": action,
                "requires": [cap["capability"]],
            })

    return {
        "schema_version": "v1",
        "pack_id": pack_id,
        "pack_version": version,
        "generated_utc": _utc_now(),
        "capabilities": capabilities,
        "activation_map": activation_map,
        "summary": {
            "capability_count": len(capabilities),
            "activation_rule_count": len(activation_map),
        },
    }


def write_capability_map(pack_path: Path, out_path: Path) -> Dict[str, Any]:
    pack = load_pack(pack_path)
    cap_map = generate_capability_map(pack)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(cap_map, indent=2) + "\n", encoding="utf-8")
    return cap_map


def _utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
