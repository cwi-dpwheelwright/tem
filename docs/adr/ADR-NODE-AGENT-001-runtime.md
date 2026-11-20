# ADR-NODE-AGENT-001: Node Agent Runtime + Transport

## Status
Accepted

## Context
Iteration-04 requires a decentralized node agent runtime that can buffer telemetry,
ingest over a secure transport, and run on constrained edge environments. The team
needs a predictable packaging model and a stable transport/protocol decision to
align contracts and gold tests.

## Decision
Use Python 3.11 as the node agent runtime with `pyproject.toml` packaging and a
simple CLI entrypoint. Use HTTPS + JSON over REST for transport, aligned to the
OpenAPI contracts in `contracts/openapi`. Telemetry payloads must conform to
`contracts/json-schema/telemetry_schema.json`. Breaking changes require a new
versioned endpoint and schema file; additive changes must remain backward compatible.

## Consequences
- Pros: portable runtime, fast iteration, and alignment with existing Python tooling.
- Cons: higher runtime overhead vs native binaries; future optimization may be needed.
