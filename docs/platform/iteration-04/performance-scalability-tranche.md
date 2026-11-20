# Performance & Scalability Tranche (Iteration 04)

## Backpressure and Bounded Queues
- Use bounded queues for ingest and action pipelines.
- Expose queue depth metrics for alerting.

## Capacity Model per Node Role
- Define baseline p95/p99 latency and throughput per role.
- Record CPU/memory targets for steady state.

## Component SLO Budgets
- Establish SLOs per critical endpoint.
- Align budgets with upstream/downstream dependencies.

## Load and Stress Testing Harness
- Use the performance smoke runner (CI) for quick signals.
- Run heavier load tests in staging with artifacts captured.
