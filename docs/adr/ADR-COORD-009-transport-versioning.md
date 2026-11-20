# ADR-COORD-009: Transport Protocol + Plugin Versioning

## Status
Accepted

## Context
Coordination transports must support baseline REST APIs while allowing future
streaming or plugin-based telemetry transports. Versioning must be explicit to
avoid mismatched transports across nodes.

## Decision
- Use HTTP/JSON REST endpoints for control plane APIs in v1.
- Allow optional gRPC or WebSocket transports for streaming telemetry and
  event feeds, gated behind explicit transport negotiation.
- Require transport payloads to include a `transport_version` field where
  applicable, and continue to enforce `X-Coordination-Protocol` for request
  versioning.

## Consequences
- Pros: stable REST surface with room for transport evolution.
- Cons: multiple transports require careful client compatibility testing.
