import argparse
import hashlib
import json
from pathlib import Path


def compute_hash(prev_hash: str, event_type: str, payload_json: str) -> str:
    data = f"{prev_hash}:{event_type}:{payload_json}".encode("utf-8")
    return hashlib.sha256(data).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify coordination ledger hash chain")
    parser.add_argument("--input", required=True, help="Path to ledger events JSON")
    args = parser.parse_args()

    path = Path(args.input)
    events = json.loads(path.read_text(encoding="utf-8"))

    if not isinstance(events, list):
        raise SystemExit("Input must be a JSON list of ledger events.")

    prev_hash = ""
    for idx, event in enumerate(events):
        event_type = event.get("event_type", "")
        payload_json = event.get("payload_json", "")
        expected = compute_hash(prev_hash, event_type, payload_json)
        actual = (event.get("hash") or "").lower()
        if expected != actual:
            raise SystemExit(f"Hash mismatch at index {idx}: expected {expected}, got {actual}")
        prev_hash = actual

    print("OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
