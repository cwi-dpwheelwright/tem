# Load/Soak Plan (Iteration 02)

## Targets
- Router: target RPS and duration.
- Orchestrator: target RPS and duration.
- Gateway: target RPS and duration.
- Vector shard: target QPS and duration.
- Model worker: target QPS and duration.

## Success criteria
- p95/p99 within targets.
- Error rate below threshold.
- No sustained memory/CPU growth.

## Evidence
- Capture load logs and charts under `artifacts/load-soak/`.
