# Physics Guardrails False Positives/Negatives Runbook

## Purpose
Provide a consistent response when guardrails over-block or under-block actions.

## Triage
- Confirm policy mode and guardrail configuration.
- Inspect trace payload for guardrails_summary and pointers.
- Verify ledger entries for guardrails.validation events.

## False Positive (over-block)
1. Capture trace_id and policy_version.
2. Review guardrail violations and payload context.
3. Adjust bounds/compatibility rules in a test environment.
4. Re-run with fixtures before deploying changes.

## False Negative (under-block)
1. Capture trace_id and related payloads.
2. Add regression fixture to guardrails tests.
3. Tighten guardrail rules and re-run tests.
4. Document the change and notify stakeholders.

## Evidence
- Test: tools/cwi-gold-tests/tests/test_guardrails.py
- Ledger events: guardrails.validation
