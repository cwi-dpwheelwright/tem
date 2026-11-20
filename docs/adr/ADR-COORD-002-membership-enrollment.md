# ADR-COORD-002: Membership + Enrollment Handshake

## Status
Accepted

## Context
Nodes must enroll before participating in coordination rounds. Enrollment needs
to be auditable and support rotation/revocation.

## Decision
Expose enrollment, rotation, and revocation endpoints under `/api/membership/*`.
Enrollment persists membership records and appends audit events to a hash-chained ledger.

## Consequences
- Pros: explicit membership lifecycle and auditability.
- Cons: requires operational controls for enrollment approvals.
