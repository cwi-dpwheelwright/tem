# Ledger Hash-Chain Specification

This document defines the append-only hash-chain used to make ledger events independently auditable.

## Canonicalization
- Each event is canonicalized with **RFC 8785** before hashing.
- The `hash` field is computed over the event **without** the `hash` field itself.

## Chain Fields
- `previous_hash` (string, SHA256 hex)
- `hash` (string, SHA256 hex)

## Genesis Rules
- The first event in a file uses:
  - `previous_hash = "0000000000000000000000000000000000000000000000000000000000000000"`

## Hash Computation
1. Remove the `hash` field from the event.
2. Canonicalize the event JSON (RFC 8785).
3. Compute:
   ```
   hash = SHA256(previous_hash + "\n" + canonical_json)
   ```
4. Store the result in the `hash` field.

## Verification Algorithm
For each event in order:
1. Confirm `previous_hash` matches the prior event’s `hash` (or genesis zero-hash).
2. Recompute the `hash` using the same algorithm and compare.
3. Any mismatch fails verification and indicates tampering or corruption.

## Notes
- JSONL order is authoritative.
- Events MUST be appended only; no in-place edits.
