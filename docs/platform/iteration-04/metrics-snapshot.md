# Metrics Snapshot (Iteration 04)

This snapshot defines the minimal metrics to freeze for parity evidence.

## Required Metrics
- Request counts and error rates for /api/ask and /api/action.
- Approval ticket counts (created/approved/denied).
- Ledger append counts and verification failures.

## Capture Procedure
1. Run the perf smoke job: `OPS/cwi-ops/ci/run-perf-smoke.ps1`.
2. Export metrics from the telemetry system (Prometheus/Grafana or OTLP collector).
3. Store artifacts alongside CI logs with timestamps.
