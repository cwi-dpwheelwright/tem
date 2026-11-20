# ADR-COORD-003: Leader Election + Round Orchestration

## Status
Accepted

## Context
Rounds require a coordinator for orchestration and audit logging. We need a
lightweight leader election that supports decentralized operation.

## Decision
Use a lease-based leader election with a short TTL. The active leader is responsible
for creating rounds and accepting heartbeats.

## Consequences
- Pros: simple implementation, no external dependency required.
- Cons: requires clock discipline and lease renewal monitoring.
