# Ledger Event Schema (Minimum Fields)

Ledger events are written as JSON Lines (JSONL). Each line is a single JSON object.

## Required Fields (Minimum)
- `event` (string)
- `timestamp_utc` (string, RFC3339)
- `pack_id` (string)
- `version` (string, SHA256 hex) **or** `resolved_hash` (string, SHA256 hex)
- `verified` (boolean)

## Optional Fields
- `actor` (string)
- `details` (object)
- `evidence_sha256` (string, SHA256 hex)
- `signature` (string)
- `signature_algorithm` (string)
- `previous_hash` (string, SHA256 hex) — for hash-chain
- `hash` (string, SHA256 hex) — for hash-chain

## Example: PACK_ADOPTED
```json
{
  "event": "PACK_ADOPTED",
  "timestamp_utc": "2026-03-02T21:15:43Z",
  "pack_id": "fixture-pack",
  "version": "bf05e724673d0d493c1818c7923b5e4d9f9a0c87786d9398a0ccde7aa750491b",
  "verified": true,
  "evidence_sha256": "e7a7...f1"
}
```

## Example: PIN_APPLIED
```json
{
  "event": "PIN_APPLIED",
  "timestamp_utc": "2026-03-02T21:16:01Z",
  "pack_id": "fixture-pack",
  "resolved_hash": "bf05e724673d0d493c1818c7923b5e4d9f9a0c87786d9398a0ccde7aa750491b",
  "verified": true,
  "details": {
    "pin_order": "experiment,approved,latest_stable",
    "pin_source": "latest_stable"
  }
}
```
