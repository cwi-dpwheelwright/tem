# Extension Governance & Compatibility (Iteration 04)

## Schema Versioning + Compatibility Matrix
- Follow the compatibility matrix in `docs/platform/iteration-04/schema-compatibility-matrix.md`.
- Breaking changes require a new schema version and parallel support.

## Pack Signing + Verification (Scaffold)
- Packs must be anchored via `anchor.txt`.
- Validation script: `OPS/cwi-ops/compose/pack-service/validate-packs.sh`.
- Add signing metadata when available; verify on ingest.

## Connector Quarantine + Compatibility Guarantees
- Quarantine connectors on schema violations or repeated failures.
- Require fixtures for all connector outputs under `contracts/fixtures/`.

## Extension Point ADRs
- Maintain ADRs for extension points (packs, connectors, plugins).
- Reference the patent crosswalk for alignment: `docs/platform/iteration-04/patent-crosswalk.md`.

## Compatibility Test Matrix
- Validate extension outputs against JSON Schemas.
- Require fixtures for each supported extension type.
