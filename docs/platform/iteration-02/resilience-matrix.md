# Resilience Test Matrix (Iteration 02)

## Dependency failures
- Redis down
- Postgres down
- Vector shard unavailable
- Model worker unavailable

## Network conditions
- High latency
- Partial packet loss
- Partition between gateway and orchestrator

## Expected behavior
- Fail closed for approvals/actions.
- Retry bounds respected.
- User-facing error surfaced with trace_id.

## Evidence
- Store results under `artifacts/resilience/`.
