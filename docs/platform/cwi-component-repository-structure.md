# CWI Component Repository Structure

## Top-level
- `src/backend`: Backend services and shared libraries.
- `src/frontend`: Web and mobile UI clients.
- `OPS/cwi-ops`: Compose bundles, runbooks, and operational scripts.
- `contracts`: OpenAPI + JSON schema + fixtures.
- `tools`: Test runners and dev tooling.
- `docs`: Product, ops, and platform documentation.

## Backend layout
- `src/backend/services`: Service code (router, orchestrator, gateway, etc).
- `src/backend/common/Cwi.Common`: Shared backend libraries used across services.

## Conventions
- Services own API surface area; contracts live in `contracts/`.
- Operational configs and runbooks live in `OPS/cwi-ops/`.
- Shared libraries are versioned and referenced via solution/package locks.
