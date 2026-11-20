# ADR-TEACHER-NODE-002: Teacher Node Test Strategy

## Status
Accepted

## Context
Teacher-node tooling is the source of truth for pack validation, anchors, dependency
graphs, and publish/rollback metadata. The CI pipeline must guarantee deterministic
artifacts and prevent drift between pack.json and anchor.txt.

## Decision
Adopt a layered test strategy:
- Unit tests for anchor generation, schema validation, dependency graph output,
  and publish/rollback file creation.
- CI deterministic checks against the compose pack fixtures to ensure anchors match.
- Evaluation pipeline script that runs gold tests after optional fixture ingestion.

## Consequences
- Pros: deterministic pack artifacts and early regression detection in CI.
- Cons: requires jsonschema and pytest available in CI.
