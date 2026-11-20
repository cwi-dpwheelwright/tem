# ORR Runbook: cwi-coordination-layer (container: internal-01-core-coordination-layer)

## Pre-flight
- Confirm trust bundle mounted at `/etc/cwi/pki/trust-bundle.pem`.
- Verify required dependencies are reachable (see `Dependencies:Urls`).
- Check enrollment and round APIs in staging.
- Apply latest DB migrations (current: `0001_coordination_tables.sql`, `0002_placement_metrics.sql`).
- Ensure `COORDINATION_DB_CONNECTION` is set for durable storage.

## Startup
1. Deploy container via compose bundle.
2. Confirm `/health` returns 200.
3. Confirm `/api/ready` returns 200 after dependencies are reachable.

## TLS / mTLS
- `Transport:RequireClientCertificate=true` enforces client certs.
- Rotate trust bundle by replacing the mounted file and restarting the container.

## Membership and rounds
- Enroll nodes with `/api/membership/enroll`.
- Rotate keys with `/api/membership/rotate`.
- Revoke nodes with `/api/membership/revoke`.
- Create rounds with `/api/rounds` and heartbeat via `/api/rounds/{id}/heartbeat`.

## Rollback
- Stop container.
- Restore previous image tag and configuration.

## Evidence
- Capture enrollment and round API responses.
- Preserve ledger events for membership/round actions.
