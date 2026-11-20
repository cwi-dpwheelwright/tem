# Full E2E Simulation Plan (Iteration-09)

## Goals
- Exercise every application against a running full-stack environment with no mocks
- Use seeded data and real service calls for all flows
- Produce repeatable artifacts and JUnit outputs

## Execution Model
- Bring up full stack via `OPS/cwi-ops/compose/full`
- Seed identity, tenants, packs, and sample documents
- Run gold-tests, UI tests, and mobile tests in a single orchestrated run
- Collect artifacts under `artifacts/test-results/iteration-09-*`

## Coverage Matrix
| Application | Primary Flows | Harness | Evidence |
| --- | --- | --- | --- |
| Gateway, Router, Orchestrator | ask/action, telemetry ingest, ledger queries, reconciliation | tools/cwi-gold-tests | artifacts/test-results/iteration-09-backend-*.junit.xml |
| Identity Server + Admin UI | login, tenant bootstrap, user and role management | Playwright + API seed | artifacts/test-results/iteration-09-identity-*.junit.xml |
| Glassbox Web UI | documents, approvals, break-glass, trace lookup, feedback | Playwright | artifacts/test-results/iteration-09-node-ui-*.junit.xml |
| Pack Service + Vector Shard | pack index and anchors, vector queries | tools/cwi-gold-tests | artifacts/test-results/iteration-09-packs-*.junit.xml |
| Teams Bot | webhook validation, approvals routing, notifications | bot simulation harness | artifacts/test-results/iteration-09-teams-bot-*.junit.xml |
| Edge Agent + Connectors | ingestion to orchestrator and vector shard | tools/cwi-gold-tests | artifacts/test-results/iteration-09-ingest-*.junit.xml |
| Node Agent Runtime | activation, status, degradation events | tools/cwi-gold-tests | artifacts/test-results/iteration-09-node-agent-*.junit.xml |
| Android App | login, ask/action, telemetry sync | androidTest | artifacts/test-results/iteration-09-android-*.junit.xml |
| iOS CarPlay | login, ask, history | XCUITest | artifacts/test-results/iteration-09-ios-*.junit.xml |

## No-Mock Policy
- E2E suites must use real service calls and seeded data
- Any remaining unit-test mocks must stay inside test-only paths and be allowlisted
- External third-party integrations must use controlled test environments or protocol simulators

## Artifacts
- Store logs, screenshots, and JUnit under `artifacts/test-results/iteration-09-*`
- Store API seed payloads and run metadata alongside the test artifacts
