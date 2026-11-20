# Perf Validation Matrix (Iteration 04)

This matrix defines how to capture p95/p99 latency and throughput per subsystem.

## Harness
- Use `OPS/cwi-ops/ci/run-perf-smoke.ps1` for baseline HTTP flows.
- For service-specific benchmarks, run locally with representative input fixtures.

## Subsystems
- **cwi-android**: mobile API call latency via gateway; measure p95/p99.
- **cwi-ios-carplay**: mobile API call latency via gateway; measure p95/p99.
- **cwi-glassbox-web-ui**: frontend API call latency via gateway; measure p95/p99.
- **cwi-teams-bot**: approval webhook response time and throughput.
- **cwi-teacher-node**: report generation request latency and throughput.
- **cwi-connectors**: ingest pipeline throughput per connector.
- **cwi-etl**: ingest endpoint latency and batch throughput.
- **cwi-edge-agent**: ingest client batching latency and throughput.
- **cwi-node-agent-runtime**: activation handshake + local inference latency.
- **cwi-coordination-layer**: membership/enrollment round-trip latency.

## Evidence
- Record run timestamps, configuration, and artifacts under `artifacts/perf/`.
