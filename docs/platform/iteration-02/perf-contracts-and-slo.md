# Performance Contracts and SLOs (Iteration 02)

## Core endpoints
- `/api/ask`: p95/p99 target, throughput, error budget.
- `/api/action`: p95/p99 target, throughput, error budget.
- `/api/approval/*`: p95/p99 target, throughput, error budget.

## Vector/model latency
- Vector search p95/p99 target.
- Model inference p95/p99 target.

## Throughput and queue bounds
- Router/orchestrator queue depth limits.
- Backpressure thresholds and expected behavior.

## Evidence
- Store load/soak results in `artifacts/perf/` with timestamps.
