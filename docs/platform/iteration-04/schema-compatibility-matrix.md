# Schema Compatibility Matrix (Iteration 04)

This matrix defines compatibility expectations for contract changes.

## OpenAPI
| Change Type | Compatibility | Required Action |
|---|---|---|
| Add new optional field | Backward-compatible | Update fixtures + regenerate types |
| Add new endpoint | Backward-compatible | Update fixtures + tests |
| Remove field or endpoint | Breaking | New major version + parallel contract |
| Change field type | Breaking | New major version + parallel contract |

## JSON Schema
| Change Type | Compatibility | Required Action |
|---|---|---|
| Add optional property | Backward-compatible | Update fixtures |
| Add required property | Breaking | New schema version |
| Tighten enum/regex | Breaking | New schema version |
| Relax constraints | Backward-compatible | Update fixtures |

## Evidence Artifacts
- Frozen OpenAPI contracts: `contracts/openapi/`
- JSON Schemas: `contracts/json-schema/`
- Fixtures: `contracts/fixtures/`
