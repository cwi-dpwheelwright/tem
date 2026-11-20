# ADR-COORD-007: Backhaul Protection Controls

## Status
Accepted

## Context
Mesh backhaul must enforce authenticated channels and optionally restrict which
interfaces are allowed for coordination traffic.

## Decision
Maintain a backhaul policy with:
- require_authenticated_channel
- allowed_interfaces (optional allow-list)
- policy_version

Expose policy get/set endpoints and an evaluation endpoint for runtime checks.

## Consequences
- Pros: centralized enforcement and audit-friendly policy versioning.
- Cons: per-tenant policies may require additional storage and access controls later.
