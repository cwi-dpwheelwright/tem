# ORR Runbook: cwi-router (container: internal-01-core-router)

## Pre-flight
- Verify env vars (see `OPS/ENV_VAR_MATRIX.md`).
- Confirm orchestrator URL is reachable.

## Startup
1. Deploy container via compose bundle.
2. Confirm `/api/health` returns 200.

## Readiness
- `/api/health` returns status `ok`.
- `/api/metrics` reachable (may be stub during bootstrap).

## Model tier routing
Ensure the model tier endpoints are configured:
- `MODEL_PRO_URL` (GPU tier)
- `MODEL_FREE_URL` (CPU overflow tier)
- `MODEL_HEALTH_PATH` (default: `/api/tags`)
- `MODEL_CB_FAILURE_THRESHOLD` and `MODEL_CB_COOLDOWN_SECONDS`

Example (local):
```
MODEL_PRO_URL=http://cwi-gpu-worker-pro:11435
MODEL_FREE_URL=http://cwi-gpu-worker-free:11436
MODEL_HEALTH_PATH=/api/tags
```

## Rollback
- Stop container.
- Restore previous image tag.

## Evidence
- Capture logs and health check output.
