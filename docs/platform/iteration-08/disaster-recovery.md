# Disaster Recovery (Iteration-08)

## Targets
- RTO: 4 hours for core API (router/orchestrator)
- RPO: 24 hours for Postgres (or tighter if required)

## Recovery steps
1. Restore Postgres using `OPS/cwi-ops/runbooks/backup-restore.md`.
2. Redeploy core services with last known-good image tags.
3. Validate health/ready endpoints and run smoke tests.

## Evidence
- Backup artifact checksums
- Restore logs and validation outputs
