# Ledger Verification Runbook

## Purpose
Verify ledger integrity and surface failures for alerting.

## Preconditions
- Orchestrator reachable.
- `CWI_ORCHESTRATOR_URL` set.
- `CWI_AUTH_TOKEN` set if required.

## Manual run
```powershell
$env:CWI_ORCHESTRATOR_URL="http://localhost:18082"
$env:CWI_AUTH_TOKEN="token"
$env:CWI_TRACE_IDS="trace-1,trace-2"
OPS/cwi-ops/scripts/ledger-verify.ps1 -TraceLimit 25 -OutputDir artifacts/ledger-verify
```

## Optional: verify explicit trace IDs
```powershell
OPS/cwi-ops/scripts/ledger-verify.ps1 -TraceIds trace-1,trace-2 -OutputDir artifacts/ledger-verify
```

## Scheduling
- Run hourly in production and daily in staging.
- Configure alerting on non-zero exit codes and artifacts containing failed trace IDs.

## Evidence
- JSON output in `artifacts/ledger-verify/`.
