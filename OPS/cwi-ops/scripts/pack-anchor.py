import argparse
import hashlib
import json
import sys
from pathlib import Path


def canonicalize_json(value) -> str:
    # Deterministic JSON serialization (RFC 8785-compatible subset).
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def compute_anchor(pack_path: Path) -> str:
    data = json.loads(pack_path.read_text(encoding="utf-8"))
    canonical = canonicalize_json(data)
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    return digest


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate anchor.txt for pack.json")
    parser.add_argument("pack", help="Path to pack.json")
    parser.add_argument("--out", help="Path to anchor.txt (default: sibling anchor.txt)")
    args = parser.parse_args()

    pack_path = Path(args.pack)
    if not pack_path.exists():
        print(f"pack.json not found: {pack_path}")
        return 1

    anchor = compute_anchor(pack_path)
    out_path = Path(args.out) if args.out else pack_path.with_name("anchor.txt")
    out_path.write_text(anchor + "\n", encoding="utf-8")
    print(anchor)
    return 0


if __name__ == "__main__":
    sys.exit(main())
