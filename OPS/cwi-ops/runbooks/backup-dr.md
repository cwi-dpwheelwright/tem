# Disaster Recovery Runbook (Backups + Restore Drill)

## Scope
Postgres backup/restore validation and quarterly restore drill checklist.

## Backup policy
- Daily full backup, retained for 30 days.
- Weekly full backup, retained for 90 days.
- Store backups in immutable storage with access logging.

## Pre-flight
- Confirm backup job succeeded within last 24 hours.
- Verify backup files are present and checksums match.
- Confirm restore target environment is isolated (staging or DR).

## Restore procedure (staging)
1. Provision clean Postgres instance.
2. Restore latest full backup.
3. Run migrations (if required) and verify schema version.
4. Validate core queries and health endpoints.
5. Run ledger verification against restored data.
6. Capture evidence (logs, checksums, query results).

## Quarterly drill checklist
- [ ] Select backup artifact and document timestamp.
- [ ] Restore into staging and validate schema + row counts.
- [ ] Run smoke workflow (ask/action) and confirm success.
- [ ] Run `OPS/cwi-ops/scripts/ledger-verify.ps1` and record results.
- [ ] Record duration, issues, and remediation steps.

## Evidence
- Backup artifact checksums.
- Restore logs and schema version.
- Validation query outputs.
