# ADR-COORD-004: Upgrade/Rollback Strategy

## Status
Accepted

## Context
Coordination nodes are deployed across edge environments. Upgrades must be safe,
and nodes must remain protocol compatible.

## Decision
Use protocol version negotiation via `X-Coordination-Protocol`. Upgrades are
rolled out in waves, and any incompatible nodes are rejected with a clear error.
Rollback is handled by redeploying the previous container image and restoring
the prior trust bundle if needed.

## Consequences
- Pros: predictable compatibility rules and simple rollback.
- Cons: requires explicit version management and deployment discipline.
