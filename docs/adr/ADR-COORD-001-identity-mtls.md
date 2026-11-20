# ADR-COORD-001: Node Identity + mTLS Trust Distribution

## Status
Accepted

## Context
The coordination layer requires authenticated node membership and secure transport.
We need a repeatable trust distribution model and a rotation procedure.

## Decision
Use mTLS for all coordination endpoints. Each node presents a client certificate
issued by a coordination CA. Trust is distributed via a signed PEM bundle mounted
read-only into each coordinator.

## Consequences
- Pros: strong node identity and transport security.
- Cons: requires operational CA management and rotation workflows.
