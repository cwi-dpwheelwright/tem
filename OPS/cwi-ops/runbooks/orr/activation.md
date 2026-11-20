# ORR: Activation Outage + Revocation + Rollback

## Scenario
Activation handshake fails for a subset of devices.

## Steps
1. Confirm failure signals in logs/metrics.
2. Issue emergency credentials for affected devices.
3. Revoke compromised credentials.
4. Roll back activation change if needed.

## Evidence
- Runbook execution logs.
- Updated activation artifacts in secure storage.
