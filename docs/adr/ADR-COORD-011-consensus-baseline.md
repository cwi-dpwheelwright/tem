# ADR-COORD-011: Consensus Baseline

## Status
Accepted

## Context
The coordination layer needs a baseline consensus approach for membership and
round orchestration. Full consensus in v1 adds operational complexity.

## Decision
- Start with a single leader coordinator instance (see ADR-COORD-003).
- Defer distributed consensus to a later iteration (e.g., Raft-backed store).
- Use deterministic audit logging so replicas can reconcile if added later.

## Consequences
- Pros: simpler operations and faster delivery in v1.
- Cons: leader failure requires manual failover or external orchestration.
