# Orchestrator Pack Contract Compatibility (2026-03-12)

## Summary
The orchestrator now supports both legacy pack contracts and the adopted/publication contract, with explicit resolver strategy selection and shared canonical anchor verification. Legacy behavior is preserved, and adopted packs resolve via adoption metadata without requiring host-side shims.

## What Changed
- Added a normalized `PackDescriptor` and `PackContractType` to capture requested vs resolved name, version, hash/anchor, artifact locations, and entries/modules.
- Introduced resolver strategies:
  - `LegacyPackResolver` (legacy `/packs/{pack}/pack.json` + `anchor.txt`)
  - `AdoptedPackResolver` (adoption manifest + publication file)
  - `PackResolver` now composes strategies based on `PACK_RESOLVER_MODE`.
- Added shared `PackAnchor` canonicalizer for anchor hashing (used by pack fetch and ingest).
- Updated manifest parsing to recognize `pack_target` and `publication.slug`, plus entries/modules extraction.
- Added structured resolver diagnostics and error messages for missing adoption metadata and anchor mismatches.
- Added comprehensive logging for resolver chain selection, manifest lookups, name mappings, dependency resolution, and anchor mismatch diagnostics.

## Compatibility Behavior
- Default mode is `auto` (adopted-first, fallback to legacy on manifest/availability errors).
- Legacy packs (`fixture-pack`, `extrusion-pack`, etc.) still resolve via `/packs/{pack}/pack.json`.
- Adopted packs resolve via `current_adoption.json` and `publication.published_file` (or `latest_file`).

## Configuration Knobs
- `PACK_RESOLVER_MODE`: `auto` (default), `legacy`, `adopted`, `legacy_first`, `adopted_first`
- `PACKS_ADOPTED_BASE_URL`: optional base URL for adoption metadata (defaults to `PACKS_BASE_URL`)
- `PACKS_ADOPTED_MANIFEST_PATH`: optional path or absolute URL (default `/packs/adopted/current_adoption.json`)

## Tests
- `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Cwi.Orchestrator.UnitTests.csproj -c Release`

## Remaining Risks / Open Questions
- Confirm the exact adopted pack JSON structure from production manifests (fields beyond `pack_target`, `publication.*`, `entries`) and extend parsing if needed.
- Validate whether adopted pack anchor hashes are computed with or without `anchor_hash` present in the manifest payload.
