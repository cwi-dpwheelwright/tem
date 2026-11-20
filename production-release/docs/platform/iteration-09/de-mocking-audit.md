# De-Mocking Audit Plan (Iteration-09)

## Goal
Eliminate stubbed or mocked data and placeholder URLs from runtime code, configs, fixtures, and E2E flows. Allow only explicitly scoped test doubles in unit-test-only paths.

## Scope
- Runtime services and clients under `src/backend`, `src/frontend`, `src/edge-agent`, `src/connectors`, `src/node-agent-runtime`, `src/mobile`, `src/etl`
- Contracts and fixtures under `contracts/fixtures` and `contracts/json-schema`
- Example configuration files and READMEs that hardcode localhost or example.com
- Test suites and harnesses to ensure mocks are isolated or replaced with seeded real data

## Detection Rules
- Disallow hard-coded URLs for `localhost`, `127.0.0.1`, and `example.com` or `example.net` or `example.org` in runtime code and configs
- Flag keywords `stubbed`, `mock`, `fake`, `dummy`, and `placeholder` outside test-only paths
- Flag defaults in code that silently fall back to local URLs
- Flag fixtures that contain stubbed text or empty placeholder values

## Remediation Playbook
- Replace stubbed fixtures with real samples captured from gold-tests or E2E runs
- Replace default localhost URLs with required configuration and fail fast on missing values
- Convert mock-based flows to seed-and-run integration tests where feasible
- For remaining unit-test mocks, keep them in test-only paths and list them in an allowlist

## Hotspot Inventory (Initial Scan)
- `contracts/fixtures/orchestrator/ask-response.json` contains "stubbed answer"
- `src/frontend/web-ui/cwi-identity-admin/src/api/client.ts` uses a fallback base URL
- `src/frontend/web-ui/cwi-identity-admin/src/pages/Login.tsx` uses a local default and placeholder
- `src/backend/services/cwi-teams-bot/src/config.ts` has a fallback gateway URL
- `src/mobile/android/app/build.gradle.kts` defines a default API base URL
- `src/mobile/ios-carplay/Sources/AppDefaults.swift` defaults to a local base URL
- `src/node-agent-runtime/configs/node-agent.example.yaml` contains localhost URLs
- `src/connectors/configs/connectors.example.yaml` contains localhost URLs
- `src/edge-agent/configs/edge-agent.example.yaml` contains localhost OTLP endpoint

## Gates and Evidence
- Add repo-wide scan script under `OPS/cwi-ops/scripts` with an allowlist for test-only paths
- Store scan results under `artifacts/qa/iteration-09-stub-scan-*.txt`
- CI gate fails on new stub or mock or placeholder URL violations
