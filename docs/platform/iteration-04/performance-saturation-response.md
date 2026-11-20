# Performance Evidence & Saturation Response (Iteration 04)

## Endpoint SLO Budgets
- Define SLOs for /api/ask, /api/action, and approval completion.
- Track p95/p99 latency and error budgets.

## Load Testing Evidence
- Capture load test reports with timestamps and configuration.
- Store artifacts alongside CI logs.

## Saturation Response Rules
- When saturation is detected, shed load and fail closed for actions.
- Emit ledger/audit events for saturation-triggered denials.
