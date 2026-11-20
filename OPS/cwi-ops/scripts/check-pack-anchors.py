import argparse
import hashlib
import json
import sys
from pathlib import Path


def canonicalize_json(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def compute_anchor(pack_path: Path) -> str:
    data = json.loads(pack_path.read_text(encoding="utf-8"))
    canonical = canonicalize_json(data)
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return digest


def check_pack_dir(pack_dir: Path) -> tuple:
    pack_path = pack_dir / "pack.json"
    anchor_path = pack_dir / "anchor.txt"
    if not pack_path.exists() or not anchor_path.exists():
        return False, f"Missing pack.json or anchor.txt in {pack_dir}"

    expected = compute_anchor(pack_path)
    actual = anchor_path.read_text(encoding="utf-8").strip()
    if expected != actual:
        return False, f"Anchor mismatch in {pack_dir}: expected {expected}, got {actual}"

    return True, "OK"


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate pack anchors for all packs under a root directory")
    parser.add_argument("--packs-root", default="OPS/cwi-ops/compose/packs")
    parser.add_argument(
        "--ignore",
        action="append",
        default=[],
        help="Directory names to ignore (repeatable or comma-separated).",
    )
    args = parser.parse_args()

    root = Path(args.packs_root)
    if not root.exists():
        print(f"Packs root not found: {root}")
        return 1

    ignore = set()
    for entry in args.ignore:
        for value in (entry or "").split(","):
            value = value.strip()
            if value:
                ignore.add(value)

    failures = 0
    for pack_dir in sorted([p for p in root.iterdir() if p.is_dir()]):
        if pack_dir.name in ignore:
            continue
        ok, msg = check_pack_dir(pack_dir)
        if not ok:
            failures += 1
            print(msg)

    if failures:
        return 1

    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
