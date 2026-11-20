# Runbook: Pack Lifecycle Operations

## Purpose
Provide operational steps to ingest, activate, rollback, retire, and delete packs using the orchestrator APIs and the admin console.

## Mirror flow (Teacher MinIO -> Core Pack Service)
If packs are published by the Teacher node to MinIO, Core uses a mirror job to materialize them into `/srv/cwi/packs` for Pack Service to serve.
Runbook: `OPS/cwi-ops/runbooks/pack-mirror-minio.md`.

## Preconditions
- Orchestrator API is running and reachable.
- Pack storage volume is mounted and writable (e.g., `PACKS_DIR`).
- Admin console is configured with admin scopes and break-glass token for destructive actions.

## Key endpoints
- Ingest: `POST /api/ingest/skillpack`
- Activate: `POST /api/packs/activation`
- Rollback: `POST /api/packs/rollback`
- Retire: `POST /api/packs/retire`
- Delete: `POST /api/packs/delete`
- Audit: `GET /api/packs/audit`

## Quick checks
1. Orchestrator health:
   `curl http://localhost:18081/api/health`
2. Admin console loads:
   `http://localhost:18091`
3. Pack registry index available:
   `OPS/cwi-ops/compose/packs/index.json`

## Standard operations
### 1) Ingest (upload + validate)
- Use the admin console pack upload flow.
- For API use, send a multipart form with the pack bundle and metadata.
- Confirm response includes `pack_id`, `version`, and `anchor_hash`.

### 2) Activate
- Use the admin console activation panel and select tenant + version.
- Provide a reason and a break-glass token for destructive actions (if required).
- Confirm response includes updated activation status and timestamp.

### 3) Rollback
- Use the admin console rollback action to revert to a prior version.
- Verify the response and confirm the active version in the registry index.

### 4) Retire (soft)
- Use the retire action to mark a version as retired.
- Retired versions should be hidden from public catalogs but remain in storage.

### 5) Delete (hard)
- Use the delete action for permanent removal.
- Confirm you selected the correct pack/version; deletion removes assets from storage and updates the index.

## Audit verification
- Use the admin console audit log view to confirm actor, action, pack, version, and timestamp.
- API validation:
  `GET /api/packs/audit?pack_id=...&from_utc=...`

## Failure modes + actions
- **Ingest validation fails**
  - Verify the bundle conforms to `pack_schema.json`.
  - Re-run with `dry_run=true` to inspect errors.
- **Index not updated**
  - Confirm write permissions on `PACKS_DIR`.
  - Check orchestrator logs for index writer errors.
- **Activation/rollback fails**
  - Verify tenant headers and break-glass token.
  - Confirm the target version exists in the registry index.
- **Retire/delete fails**
  - Ensure the pack/version exists and is not locked by active workflows.
  - Check audit log for partial updates; retry only after confirming state.

## Rollback guidance
- If a newly activated pack causes regressions, rollback to the prior version using the admin console.
- If delete was executed incorrectly, restore the pack from backup and re-ingest.

## References
- Admin console pack UI: `src/frontend/web-ui/cwi-admin-console/src/components/packs/*`
- Orchestrator pack controllers: `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Controllers/*`
- Decommission plan: `docs/platform/iteration-10/identity-admin-deprecation.md`
