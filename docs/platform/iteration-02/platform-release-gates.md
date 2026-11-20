# Platform Release Gates (Iteration 02)

This document defines baseline gates and evidence artifacts for iteration-02 platform epics.

## Release evidence gating (contracts, fixtures, tests)
- Contracts: OpenAPI + JSON Schema must match generated types (no drift).
- Fixtures: conformance vectors must pass in gold tests.
- Tests: unit, integration, smoke, and simulation suites must pass.
- Evidence: CI logs + artifacts stored in `artifacts/` with timestamps.

## Definition of done for mission-critical items
- Implementation complete and verified with tests.
- Required docs (runbooks, checklists) exist.
- Performance targets met with evidence.
- Security gates passed (SAST/secret scan/dependency scan).

## Mission-critical release gate
- All P0 items are in `done`.
- Break-glass and kill-switch paths validated.
- Approval gating parity validated across surfaces.

## Performance contracts and resilience evidence
- Define p95/p99 latency and throughput per subsystem.
- Execute load tests and store reports.
- Document resilience scenarios (dependency failures, timeouts).

## ORR gates by component
- Each component has an ORR checklist and sign-off.
- Runbooks include on-call, rollback, and escalation steps.

## Performance & capacity (Router/Orchestrator/Gateway/Vector Shard/Model Worker)
- Track concurrency targets, queue depth, memory/CPU.
- Document capacity limits and saturation behavior.

## Invariant parity artifacts per subsystem
- Pack trust, trace contract, approval gating, provenance ledger, kill-switch parity validated via gold tests.
- Evidence stored under `artifacts/` with run metadata.

## Implementation readiness baselines
- Dependency inventory complete.
- Required infra in place (DB, Redis, telemetry, secrets).

## HA/DR readiness
- Backups tested and documented.
- Failover and restore procedures validated.

## Secure SDLC gates
- SAST, dependency scanning, secret scanning enforced in CI.
- Security review checklist completed per release.

## Unified performance harness
- Single harness to run perf tests across services.
- Standard report format for p95/p99 + error rates.
