# Acceptance Criteria Templates (Iteration 04)

## Invariant Template (General)

**Invariant Name**
- Example: "Ledger events are append-only and hash-chained"

**Scope**
- Services/components covered
- Entry points / endpoints impacted

**Preconditions**
- Required configuration (env vars, feature flags)
- Required fixtures or contracts
- Required dependencies running

**Steps**
1. Trigger the invariant behavior (API call or workflow).
2. Observe outputs (status codes, payloads).
3. Verify evidence artifacts (logs/ledger/events/metrics).

**Expected Results**
- Output behavior (success/fail/deny)
- Evidence artifacts present with required fields
- Fail-closed behavior when dependencies are missing

**Evidence**
- Test or script name
- Fixture paths
- Screenshot/log excerpt location

**Failure Modes**
- Explicit failure conditions and expected error codes
- Audit/ledger event required on failure

**Owner / Review**
- Owner team
- Review date

## Kill-Switch Non-Bypassable Template

**Invariant Name**
- "Kill-switch blocks all ACTION execution across ingress paths"

**Scope**
- Router, Orchestrator, Gateway
- All ACTION endpoints (including delegated/ownership flows)

**Preconditions**
- Kill-switch enabled (explicit flag)
- Auth token available for valid actor
- All dependencies up (so failures are attributable to kill-switch)

**Steps**
1. Enable kill-switch (documented mechanism).
2. Submit ACTION request with valid auth.
3. Attempt approval completion if a ticket is issued.

**Expected Results**
- ACTION is blocked (explicit denial response).
- No action execution side effects occur.
- Ledger/event emitted indicating kill-switch block with trace_id.
- Break-glass or bypass attempts are denied and logged.

**Evidence**
- Test name and path
- Ledger event type + required fields
- Trace payload snippet showing denial

**Failure Modes**
- Any ACTION is executed while kill-switch is enabled.
- Missing ledger/audit entry for blocked action.

**Owner / Review**
- Platform governance
- Quarterly review with incident simulation evidence
