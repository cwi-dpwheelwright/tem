# Testing Strategy + Gating

## Taxonomy
- Unit: local logic.
- Integration: service dependencies and contracts.
- Smoke: minimal critical flows.
- Simulation: fault injection and chaos.

## Gating
- No contract drift.
- Gold tests must pass.
- Required evidence recorded in `artifacts/`.

## Recursive Learning Validation
Recursive learning is release-ready only when the validation run proves the full loop:

```text
controlled input event
-> process miner observation
-> normalized event
-> memory / graph candidate
-> policy decision
-> graph update
-> vector refresh
-> retrieval improvement
-> answer improvement
-> critic validation
-> Truth Engine state
-> ledger/audit proof
-> regression or response-quality update
-> repeatability check
```

Evidence for the run must conform to
`contracts/json-schema/recursive_validation_run.json` and include the sample shape in
`contracts/fixtures/orchestrator/recursive-validation-run.sample.json`.

The controlled ASK E2E suite lives in
`testing/recursive-intelligence/controlled-ask-e2e-suite.v1.json`. Use
`testing/recursive-intelligence/run-controlled-ask-e2e.py` to create
`summary.json`, `results.ndjson`, and stakeholder-ready `report.md` artifacts.
Production-like runs should authenticate through the identity API first with
`--auth-mode identity`; the runner posts to `/auth/login`, applies the returned
bearer token to `/api/ask`, and writes only redacted auth status evidence.
The controlled suite starts through `/api/ask`. If ASK returns deferred
home-node work instead of a final answer, the runner polls
`/api/ask/deferred/{work_id}?request_id=...` before grading. If the work remains
pending, the report records an unresolved deferred-completion gap rather than
treating the queue acknowledgement as recursive-learning evidence.
A controlled ASK E2E report is behaviorally valid only when `NOT RUN` is `0`;
any `NOT RUN > 0` artifact is only an execution-attempt diagnostic.

## Hard Governance Gates
These gates are zero-tolerance. Do not average them into quality pass rates.

- Pack trust: `sha256(canonical(pack.json)) == anchor.txt`.
- Frozen response trace: ASK and ACTION responses keep `sources`, `confidence`, `policy_mode`, `model`, and `pack_hash`.
- Approval gating: risky ACTION responses return an `ApprovalTicket` or denial and do not execute silently.
- Provenance ledger: protected recursive-learning events have ledger evidence or fail closed.
- Kill-switch: action execution is blocked immediately while the switch is active.
- Tenant isolation: no cross-tenant trace, cache, memory, graph, vector, or summary reuse.
- Privacy / OPSEC: seeded private canaries do not appear in prompts, logs, traces, dashboards, summaries, or external-comparison payloads.
- Rollback: graph, vector, cache, and runtime behavior return to the expected safe state while audit history remains append-only.

## Quality Thresholds
Quality thresholds are separate from hard gates. Routing, memory relevance, synthesis,
source quality, Critic calibration, cache reuse, and open-loop detection can use MVP,
Beta, and Production percentage thresholds only after every hard governance gate passes.
