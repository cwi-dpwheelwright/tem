# Compatibility & Versioning Policy (Iteration 04)

## Scope
This policy covers:
- OpenAPI contracts under `contracts/openapi/`
- JSON Schemas under `contracts/json-schema/`
- Generated client types under `contracts/generated/`

## Contract Versioning Rules
- **Backward-compatible changes** (additive fields, optional properties, new endpoints) keep the same major version.
- **Breaking changes** (removals, type changes, required field additions) require a new major version and a parallel contract.
- **Deprecated fields** remain supported for one full release cycle and must include deprecation notes.

## Compatibility Matrix (Template)
| Contract | Version | Compatible With | Notes |
|---|---|---|---|
| cwi-identity-server OpenAPI | 10H frozen | 10H clients | Additive only |
| cwi-router OpenAPI | 10H frozen | 10H clients | Additive only |
| cwi-orchestrator OpenAPI | 10H frozen | 10H clients | Additive only |
| JSON Schemas (including auth token claims/response) | 10H frozen | 10H fixtures | Additive only |

## Change Process
1. Update contract (OpenAPI or JSON Schema).
2. Regenerate client types if applicable.
3. Update fixtures and tests for new fields.
4. If breaking, create a new versioned contract file and update consumers.

## Evidence
- Frozen contracts in `contracts/openapi/`.
- Fixtures in `contracts/fixtures/`.
- Generated types in `contracts/generated/`.

## Rollback Guidance
If a contract update causes failures:
- Revert to the prior frozen contract.
- Roll back generated types and fixture updates together.
- Record the rollback reason in the iteration notes.
