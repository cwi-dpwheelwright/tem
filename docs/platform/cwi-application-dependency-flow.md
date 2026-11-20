# CWI Application Dependency Flow

## External entrypoints
- **Gateway** terminates TLS and routes requests:
  - `/api/*` → router
  - `/packs/*` → pack-service
  - `/mcp/*` → optional gateway alias that still routes through the router

## Core service flow
1. Gateway receives client traffic.
2. Router validates request and coordinates workflow.
3. Orchestrator executes ask/action workflows and records provenance.
4. Pack service serves immutable pack artifacts:
   - `/packs/{pack}/pack.json`
   - `/packs/{pack}/anchor.txt`

## Naming
- Use **cwi-orchestrator** for the orchestrator service (avoid legacy “cwi-mcp” naming).
- `/mcp/*` is an optional alias behind a feature flag and never bypasses the router.
