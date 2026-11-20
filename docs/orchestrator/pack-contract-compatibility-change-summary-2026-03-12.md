# Pack Contract Compatibility Change Summary (2026-03-12)

## Summary
This update extends the orchestrator to resolve both legacy packs and adopted/publication packs while preserving canonical hashing, trace fields, and governance invariants. Resolution is now strategy-based with explicit configuration and comprehensive logging.

## Functional Changes
- Added a normalized pack descriptor (`PackDescriptor`) and explicit contract type (`PackContractType`) to unify legacy and adopted metadata.
- Implemented resolver strategies:
  - `LegacyPackResolver` for `/packs/{pack}/pack.json` + `anchor.txt`.
  - `AdoptedPackResolver` for adoption manifest + publication file.
  - `PackResolver` composes strategies based on `PACK_RESOLVER_MODE`.
- Extended manifest parsing to support `pack_target`, `publication.*`, `entries`, and `modules`.
- Updated version extraction for adopted packs (`publication.version`) while preserving legacy version fields.
- Centralized canonicalization and hashing through `PackAnchor` to align ingest and fetch verification.

## Observability Enhancements
- Resolver chain selection and fallback decisions are logged.
- Adoption manifest discovery, entry matching, and name mapping are logged.
- Dependency resolution logs include contract type, version, and hash.
- Anchor mismatch diagnostics include computed vs authoritative hash and fallback behavior.

## Configuration Additions
- `PACK_RESOLVER_MODE`: `auto` (default), `legacy`, `adopted`, `legacy_first`, `adopted_first`
- `PACKS_ADOPTED_BASE_URL`: optional base URL for adoption metadata (defaults to `PACKS_BASE_URL`)
- `PACKS_ADOPTED_MANIFEST_PATH`: path or absolute URL (default `/packs/adopted/current_adoption.json`)

## Tests and Evidence
- Added unit tests for legacy/adopted/mixed resolution, version extraction, anchor mismatch handling, case-insensitive name mapping, and trace field propagation.
- Test command executed: `dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Cwi.Orchestrator.UnitTests.csproj -c Release`

## Files Added
- `iterations/iteration-12.csv`
- `docs/orchestrator/pack-contract-compatibility-2026-03-12.md`
- `docs/orchestrator/pack-contract-compatibility-change-log-2026-03-12.md`
- `docs/orchestrator/pack-contract-compatibility-changes-outline-2026-03-12.md`
- `docs/orchestrator/pack-contract-compatibility-change-summary-2026-03-12.md`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackContractType.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackResolverMode.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackDescriptor.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackAnchor.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/IPackContractResolver.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/LegacyPackResolver.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/AdoptedPackResolver.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Packs/PackResolverTests.cs`

## Files Updated
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/ApplicationOptions.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Configuration/OrchestratorConfig.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Program.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Composition/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackFetcher.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackResolver.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackManifestReader.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackManifestMetadata.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/PackDocument.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Packs/Ingest/PackIngestService.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`
