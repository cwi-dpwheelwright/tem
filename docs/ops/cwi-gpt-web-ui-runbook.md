# CWI GPT Web UI Runbook

## Scope
- End-user web client (Next.js) for pack-driven ask/action/trace flows.
- Dependencies: CWI Gateway, CWI Pack Service, Identity (if enabled).

## Quick Checks
1. Open `/api/health` on the deployed service and confirm HTTP 200.
2. Verify runtime configuration in `/settings` (gateway URL, pack name, and auth settings).
3. Run pack verification in `/trust` and confirm pack hash is displayed.

## Troubleshooting
### Auth failures (401/403)
- Symptoms: Redirect to `/settings`, API errors with status 401/403.
- Actions:
  1. Re-enter the bearer token in `/settings`.
  2. Confirm idle timeout settings are not expiring the session.
  3. Validate token scopes/roles with the Identity service (if in use).
- Expected logs: `api.error` with `status` and `requestId` only (no secrets).

### Pack verification failures
- Symptoms: "Pack anchor mismatch" or pack fetch errors on `/trust`.
- Actions:
  1. Confirm `pack name` and `gateway base URL` in `/settings`.
  2. Check the pack service for `pack.json` and `anchor.txt` parity.
  3. Re-run verification after the pack service is healthy.

### Rate limiting (429)
- Symptoms: Ask/Action failures with status 429.
- Actions:
  1. Wait for the retry window and submit again.
  2. Check gateway rate-limit configuration and per-tenant quotas.
  3. Capture the request id and include it in escalation.

### Timeouts (408)
- Symptoms: "Request timed out" errors from ask/action/trace.
- Actions:
  1. Increase `Request timeout (ms)` in `/settings` temporarily.
  2. Verify gateway responsiveness and downstream services.

### Trace lookup failures
- Symptoms: Trace lookup error on `/trace/[traceId]`.
- Actions:
  1. Use the Retry button.
  2. Confirm the trace id exists in gateway logs.

## Diagnostics and Escalation
- Use `/settings` ? "Copy diagnostics JSON" to export safe diagnostics.
- Include these fields in escalations:
  - build version/sha
  - gateway base URL
  - pack hash and verification time
  - last API error status and request id

## Rollback
- Redeploy the previous container image tag or build artifact.
- Clear session overrides in `/settings` to return to env defaults.
