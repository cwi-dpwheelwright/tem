# Runbook: Consent Ingest

## Purpose
Continuously ingest operator consent payloads into the consent store so operator-memory ingestion can enforce consent.

## Inputs
- Drop JSON/JSONL files into `OPS/cwi-ops/compose/full/consent-input/`.
- Each record should include `tenant_id`, `operator_id`, and `consent.granted`/`consent.granted_utc`.

## Quick checks
1. Verify container is running:
   `docker compose -f OPS/cwi-ops/compose/full/docker-compose.full.yml ps cwi-consent-ingest`
   Note: service name is `cwi-consent-ingest`; container name is `internal-01-etl-consent-ingest`.
2. Inspect logs for cycle summaries:
   `docker logs internal-01-etl-consent-ingest | Select-String consent_ingest_runner`
3. Confirm processed files moved into the archive volume.

## Failure modes + actions
- **No files processed**
  - Check the host bind paths `CWI_CONSENT_INGEST_INPUT_HOST_DIR`, `CWI_CONSENT_INGEST_ARCHIVE_HOST_DIR`, `CWI_CONSENT_INGEST_ERROR_HOST_DIR`, and `CWI_CONSENT_DB_HOST_DIR`.
  - The container paths are fixed to `/consent-input`, `/consent-archive`, `/consent-error`, and `/consent-store`.
  - Ensure files are valid JSON/JSONL.
- **Files moved to error**
  - Inspect logs for `consent_ingest_runner failed` and fix malformed payloads.
  - Requeue corrected files by copying back into the input directory.
- **Consent DB errors**
  - Confirm the mounted host directory for the consent DB is writable.
  - If DB corrupt, rotate the file and re-ingest from source.
- **Remote consent enabled but unavailable**
  - If you switched to remote consent resolution (`CWI_CONSENT_REMOTE_BASE_URL`), verify the endpoint is reachable.

## Rollback
- Stop the job: `docker compose -f OPS/cwi-ops/compose/full/docker-compose.full.yml stop cwi-consent-ingest`
- Disable schedule by setting `CWI_CONSENT_INGEST_INTERVAL_SECONDS` to a large value or `CWI_CONSENT_INGEST_RUN_ONCE=true`.
