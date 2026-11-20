# Gold Tests for Invariants (Iteration 02)

## Pack trust parity
- Validate pack canonicalization + anchor hash vectors.
- Ensure parity across router/orchestrator/UI surfaces.

## Trace contract parity
- Validate trace schema and required fields.
- Verify trace_id propagation across services.

## Approval gating parity
- Approval required paths return ApprovalTicket.
- Fail-closed behavior when dependencies unavailable.

## Kill-switch parity
- Kill-switch blocks action execution across surfaces.

## Evidence
- Run gold tests and store logs under `artifacts/gold-tests/`.
