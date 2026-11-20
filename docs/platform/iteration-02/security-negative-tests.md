# Security-negative Tests (Iteration 02)

## Auth bypass
- Requests without auth headers must fail.
- Requests with invalid tokens must fail.

## Replay protection
- Reused idempotency keys must not execute.
- Replayed approvals must not succeed.

## Downgrade resistance
- Force secure policies even when client requests lower policy.
- Verify kill-switch overrides action execution.

## Evidence
- Capture test outputs in `artifacts/security-negative-tests/`.
