# ADR-NODE-AGENT-002: Node Agent Test Strategy

## Status
Accepted

## Context
The node agent is responsible for edge buffering, secure ingest, and resilience
under unreliable networks. We need test coverage that verifies contract adherence
and failure-mode handling without slowing down iteration.

## Decision
Adopt a layered test strategy:
- Unit tests for buffer limits/drop policy, backoff, config validation, and
  revocation checks.
- Integration tests against a stub ingest endpoint to validate payload formatting,
  mTLS handshake, and retry behavior.
- Smoke tests using the edge-agent smoke script to validate end-to-end telemetry
  ingestion with a local stack.
- Simulation tests that inject failure modes: gateway unreachable, circuit breaker
  open, certificate revoked (denylist/CRL), and buffer overflow behavior.

## Consequences
- Pros: verifies resilience and contract behavior under real failure conditions.
- Cons: requires CI access to a stub gateway and CRL fixtures for full coverage.
