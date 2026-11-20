# Data Retention & Purge (Iteration-08)

## Retention targets
- Ledger events: 365 days
- Telemetry/metrics: 30 days
- Approval/audit logs: 180 days
- Connector delivery queues: 14 days (or until delivered)

## Purge mechanisms
- Implement periodic cleanup jobs for Postgres tables (ledger, telemetry).
- Use vector shard purge helper when required: `OPS/cwi-ops/scripts/purge-vector-shard.py`.

## Verification
- Record purge runs and row counts.
- Validate storage usage stays within budget.
