# ORR Runbook: cwi-orchestrator (container: internal-01-core-orchestrator)

## Pre-flight
- Verify env vars (see `OPS/ENV_VAR_MATRIX.md`).
- Confirm pack-service and router are reachable.
- Apply latest DB migrations using the orchestrator migration runner before
  starting or recreating the orchestrator container. Current latest:
  `0017_sustainability_request_records_backfill.sql`.
- If succession approvals are required, ensure tenant config includes `succession_approvers` (or `ownership.succession.approvers`).

## Startup
1. Deploy container via compose bundle.
2. Confirm `/api/health` returns 200.

## Readiness
- `/api/health` returns status `ok`.
- `/api/metrics` reachable (may be stub during bootstrap).
- Migration evidence shows all orchestrator SQL files were applied without
  `ON_ERROR_STOP` failures.

## Rollback
- Stop container.
- Restore previous image tag.

## Evidence
- Capture logs and health check output.
