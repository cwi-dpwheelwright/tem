# Provenance Completeness Gate (Iteration 04)

## Goal
Ensure provenance data is complete before releases.

## Required Fields
- `trace_id`
- `tenant_id`
- `timestamp_utc`
- `event_type`
- `actor_id` (when applicable)
- `prev_hash` + `entry_hash` for ledger events

## Gate Criteria
- All `ASK` and `ACTION` flows emit a ledger event.
- Ledger events are hash-chained and verifiable.
- Missing required fields fails the gate.

## Evidence
- Contract: `contracts/json-schema/ledger_event_canonical.json`
- Fixtures: `contracts/fixtures/orchestrator/*`
