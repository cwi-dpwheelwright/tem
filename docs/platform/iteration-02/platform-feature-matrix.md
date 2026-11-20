# Platform Feature Matrix (Iteration 02)

This matrix maps iteration-02 platform features to required evidence artifacts.

## Platform invariants + provable parity
- Gold tests for parity + failover: run gold tests with failure injection; capture artifact logs.
- Policy + approval guardrails: ensure approval gating parity across router/orchestrator; capture test evidence.

## Patent alignment evidence
- Patent 1 (Mesh Orchestration + Backhaul Protection): deterministic test suite + evidence archive.
- Patent 4 (PQC-secured provenance ledger): deterministic test suite + evidence archive.
- Patent 5 (Knowledge pack orchestration): deterministic test suite + evidence archive.
- Patent 6 (Encrypted deployment + kill-switch): deterministic test suite + evidence archive.

## Mission-critical release gate
- ORR checklist + rollback drills: component checklists + documented drill outcomes.
- Supply-chain security gates: SBOM + signing verification, dependency scanning.
- Kill-switch non-bypassable proof: negative test evidence across router/gateway/orchestrator + UI surfaces.

## Performance evidence and saturation response
- Load/soak evidence: sustained load tests with p95/p99 + throughput graphs.

## Release evidence gating
- Formal test matrix for invariants: mapping of invariants -> tests -> artifacts.

## Performance contracts and resilience evidence
- Endpoint performance contracts: p95/p99 budgets for core endpoints.
- Vector/model latency contracts: budgets for vector search + model inference.
- Resilience test matrix: failover/partition/timeouts for core dependencies.
- Router/orchestrator SLO budgets: SLOs + error budgets.
- Gateway pack distribution SLO: pack distribution targets.
- Model/vector capacity envelopes: concurrency + resource budgets.
- Resilience drills: partitions and churn test evidence.
- Explicit p95/p99 targets for core endpoints.
- Throughput and queue bounds.
- Vector query and model inference budgets.

## ORR gates by component
- Gateway ORR gate: checklist + evidence.
- Router ORR gate: checklist + evidence.
- Orchestrator ORR gate: checklist + evidence.
- Pack service + gold tests ORR gate: checklist + evidence.

## Performance & capacity (subsystem-specific)
- Router: SLOs + error budgets; load/soak evidence; capacity model; degradation playbook.
- Orchestrator: SLOs + error budgets; load/soak evidence; capacity model; degradation playbook.
- Gateway: SLOs + error budgets; load/soak evidence; capacity model; degradation playbook.
- Vector Shard: SLOs + error budgets; load/soak evidence; capacity model; degradation playbook.
- Model Worker: SLOs + error budgets; load/soak evidence; capacity model; degradation playbook.

## Invariant parity artifacts per subsystem
- Pack trust parity artifacts (gold tests + fixtures).
- Trace contract parity artifacts (gold tests + fixtures).
