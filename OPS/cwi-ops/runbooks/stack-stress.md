# Stack Stress Test Runbook

## Scope
End-to-end stress testing from the gateway through router, orchestrator, and model using `/api/ask` and `/api/action`. The runner logs per-request success/failure and produces step summaries to identify saturation limits.

## Preconditions
1. Gateway is reachable at the base URL.
2. Packs are seeded and verified (pack name used by the test must exist).
3. Auth token is available when `AUTH_REQUIRED=true` (default) or identity bootstrap credentials are set.
4. Model worker / LLM backend is reachable and healthy.

## Quick Setup
1. Seed the stack if needed:
   - `python tools/cwi-gold-tests/seeding/seed_full_stack.py --config tools/cwi-gold-tests/seeding/seed_config.sample.json`
2. Fetch an auth token (example):
   - `powershell -Command "$resp = Invoke-RestMethod -Method Post -Uri http://localhost:8085/auth/login -Body (@{ tenant_id='tenant-1'; username='admin'; password='CwiTestAdmin!2026' } | ConvertTo-Json) -ContentType 'application/json'; $env:CWI_AUTH_TOKEN = $resp.access_token"`
   - If `CWI_AUTH_TOKEN` is not set, the runner will attempt login using `IDENTITY_BOOTSTRAP_ADMIN_*` credentials.
3. Verify gateway health:
   - `curl http://localhost:18081/api/health`

## Run the Stress Test
Recommended ramp for local staging:
1. `powershell OPS/cwi-ops/ci/run-stack-stress.ps1 -BaseUrl http://localhost:18081 -PackName enterprise -StartConcurrency 4 -StepConcurrency 4 -MaxConcurrency 64 -StepDurationSeconds 60 -CooldownSeconds 10`

Remote example:
1. `powershell OPS/cwi-ops/ci/run-stack-stress.ps1 -BaseUrl https://cwigpt.com -PackName enterprise -StartConcurrency 4 -StepConcurrency 4 -MaxConcurrency 96 -StepDurationSeconds 90 -CooldownSeconds 10`

Notes:
- Use `-NoAction` to stress only `/api/ask`.
- Use `-NoAsk` to stress only `/api/action` (auth required).
- Use `-NoStopOnThreshold` to run all steps regardless of violations.
- Set `CWI_STRESS_LOG_MODE=failures` to reduce log volume.
- Default question is `Please explain the laws of physics.` Override via `CWI_STRESS_QUESTION` or `CWI_STRESS_ASK_PAYLOAD_JSON`.

## Artifacts
The runner writes:
- `artifacts/stack-stress/stack-stress-<timestamp>.json` (summary + step results)
- `artifacts/stack-stress/stack-stress-<timestamp>.jsonl` (per-request log when log mode is `all` or `failures`)

## Failure Criteria
Default thresholds are applied to overall results each step:
- Error rate > 5%
- p95 latency > 8s
- p99 latency > 12s
- Avg latency > 5s

Adjust thresholds with:
- `-MaxErrorRate`, `-MaxP95Ms`, `-MaxP99Ms`, `-MaxAvgMs`

## Evidence Capture
1. Save summary JSON and request logs.
2. Attach gateway/router/orchestrator/model logs with request IDs from the JSONL file.
3. Record concurrency step where violations begin (capacity limit).

## Cleanup
1. Archive artifacts under `artifacts/stack-stress/`.
2. Optionally prune logs if running multiple iterations.
