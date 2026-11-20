# Backup + Restore Runbook (Postgres + MinIO)

## Scope
Covers Postgres database backups and MinIO artifact storage backups.

## Postgres backup
1. Ensure backups are enabled for the Postgres instance.
2. Run a full backup and record checksum.
3. Store backups in immutable storage.

## Postgres restore (staging)
1. Provision clean Postgres instance.
2. Restore latest full backup.
3. Apply migrations if required.
4. Validate schema version and row counts.

## MinIO backup
1. Snapshot the bucket backing store.
2. Export object inventory and checksums.

## MinIO restore (staging)
1. Restore object store snapshot.
2. Validate sample artifacts (reports/docs).

## Evidence
- Backup artifact checksums
- Restore logs and validation outputs
- Ledger verification output (if applicable)
