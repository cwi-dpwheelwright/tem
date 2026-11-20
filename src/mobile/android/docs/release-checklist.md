# Android Release Checklist

## Build + QA
- Debug build green: `:app:assembleDebug`, `:app:test`, `:app:lint`.
- Instrumentation smoke test on emulator/device.
- Regression checklist completed.

## Performance & stability
- Crash-free rate target met (define threshold).
- ANR rate target met.
- API latency within target (p95/p99).

## Security & privacy
- Release build logs are redacted (no tokens).
- LOG_HTTP disabled in release.
- Verify secure token storage working.

## Rollback criteria
- Crash rate exceeds threshold.
- ANR rate exceeds threshold.
- p95 latency exceeds target for >30 minutes.
- Approval/action failures exceed threshold.

## Staged rollout plan
- Stage 0: internal dogfood (1-5% of internal users).
- Stage 1: 10% rollout for 24 hours; monitor crash/ANR/latency.
- Stage 2: 25% rollout for 24 hours; monitor approval/action success.
- Stage 3: 50% rollout for 24 hours; confirm regression checklist.
- Stage 4: 100% rollout once gates are green.
