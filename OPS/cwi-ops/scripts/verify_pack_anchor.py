#!/usr/bin/env python3
import argparse
import hashlib
import json
import pathlib
import sys


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def canonical_json_bytes(obj) -> bytes:
    s = json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return s.encode("utf-8")


def _is_sha256_hex(value: str) -> bool:
    if len(value) != 64:
        return False
    return all(ch in "0123456789abcdef" for ch in value)


def die(msg: str, code: int = 2) -> int:
    print(f"ERROR: {msg}", file=sys.stderr)
    return code


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Verify anchor.txt matches sha256(canonical(pack.json))."
    )
    parser.add_argument("pack_json", type=pathlib.Path, help="Path to pack.json")
    parser.add_argument("anchor_txt", type=pathlib.Path, help="Path to anchor.txt")
    parser.add_argument(
        "--expected",
        default=None,
        help="Optional expected hash (e.g., from latest.txt). If provided, anchor must match it.",
    )
    args = parser.parse_args()

    if not args.pack_json.exists():
        return die(f"pack.json not found: {args.pack_json}")
    if not args.anchor_txt.exists():
        return die(f"anchor.txt not found: {args.anchor_txt}")

    anchor_raw = args.anchor_txt.read_text(encoding="utf-8").strip()
    if not anchor_raw:
        return die(f"anchor.txt is empty: {args.anchor_txt}")

    anchor = anchor_raw.lower()
    if not _is_sha256_hex(anchor):
        return die(f"anchor.txt is not a 64-hex SHA-256 digest: '{anchor_raw}'")

    try:
        pack_obj = json.loads(args.pack_json.read_text(encoding="utf-8"))
    except Exception as exc:
        return die(f"pack.json is not valid JSON: {args.pack_json} ({exc})")

    computed = sha256_hex(canonical_json_bytes(pack_obj))
    if computed != anchor:
        return die(
            "anchor mismatch:\n"
            f"  computed: {computed}\n"
            f"  anchor:   {anchor}"
        )

    if args.expected is not None:
        expected_raw = args.expected.strip()
        expected = expected_raw.lower()
        if not _is_sha256_hex(expected):
            return die(f"expected hash is not 64-hex: '{expected_raw}'")
        if expected != computed:
            return die(
                "expected hash mismatch:\n"
                f"  computed: {computed}\n"
                f"  expected: {expected}"
            )

    print(f"OK: {computed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
