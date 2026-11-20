# ADR-PQC-001: PQC Signature Algorithm + Key Format

## Status
Proposed

## Context
The provenance ledger requires post-quantum signatures for append/verify operations. We need a concrete algorithm choice, key format, and integration approach that can be implemented consistently across services.

## Decision
Adopt a hybrid signing approach:
- Primary PQC signature: CRYSTALS-Dilithium (Level 2 or 3 depending on key size constraints)
- Optional hybrid mode: Dilithium + Ed25519 for backward compatibility

Key format and storage:
- Use encoded PEM-like blobs stored in a KMS or sealed config file.
- Keys referenced by `key_id`, with rotation supported by config.

## Consequences
- Adds a PQC library dependency to the orchestrator ledger signer.
- Requires schema updates to include `signature`, `signature_algorithm`, and `key_id`.
- Enables verification endpoints to validate PQC signatures and enforce trust policy.
