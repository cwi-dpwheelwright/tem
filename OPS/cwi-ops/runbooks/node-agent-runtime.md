# Runbook: CWI Node Agent Runtime

## Purpose
Deploy and validate the node-agent runtime (enrollment, activation gating, and health checks).

## Prerequisites
- Python 3.11+
- Access to coordination-layer enrollment endpoint
- Optional: mTLS client cert/key if enrollment requires it

## Quick deployment steps
1. Validate config:
   `cwi-node-agent validate-config --config /etc/cwi/node-agent.yaml`
2. Generate identity keys (if not pre-provisioned):
   `cwi-node-agent generate-identity --config /etc/cwi/node-agent.yaml`
3. Enroll with coordinator:
   `cwi-node-agent enroll --config /etc/cwi/node-agent.yaml`
4. Verify activation + kill-switch gating:
   `cwi-node-agent activation-status --config /etc/cwi/node-agent.yaml`
   `cwi-node-agent sync-kill-switch --config /etc/cwi/node-agent.yaml`
   `cwi-node-agent check-execution --config /etc/cwi/node-agent.yaml`
5. Verify health/readiness:
   `cwi-node-agent health --config /etc/cwi/node-agent.yaml`
   `cwi-node-agent serve-health --config /etc/cwi/node-agent.yaml --port 8090`

## Enrollment smoke validation
Run the smoke helper and capture artifacts:
`powershell OPS/cwi-ops/ci/run-node-agent-enrollment-smoke.ps1`

## Home delivery onboarding
For customer home-node shipments:
- Derive and persist `identity.node_id` before shipment, or set
  `identity.node_id: auto` so the runtime derives it from `/etc/machine-id`.
- Generate the customer-facing QR code from the node-linking JSON payload.
- Have the customer register, sign in to the GPT site, open `Account settings`,
  and use `Link My Node` to scan that QR code.

Canonical identity and QR payload details live in:
`OPS/cwi-ops/runbooks/home-node-linking.md`

## Failure modes + actions
- **Enrollment fails (HTTP error)**
  - Confirm `enrollment_url` and auth token config.
  - Validate mTLS cert/key paths and revocation config if enabled.
- **Activation invalid**
  - Check activation handshake issuer/audience/public key settings.
  - Ensure `sync-activation-status` can reach coordinator status endpoint.
- **Kill-switch enabled unexpectedly**
  - Verify admin URL and break-glass token env.
  - Confirm local state path is correct and not stale.

## Evidence
Store smoke output under `artifacts/qa/iteration-08-node-agent-*.txt`.
