# CWI ETL

This module provides deterministic chunking, provenance emission, document parsing, and reconciliation utilities.

## Usage
```bash
python -m cwi_etl
```

## Development
- Chunking logic: `cwi_etl/chunker.py`
- Provenance client: `cwi_etl/provenance.py`
- Ingest runner (concurrency/backoff): `cwi_etl/ingest_runner.py`
- Content identity model: `cwi_etl/identity.py`
- Parser registry + TXT/Markdown/DOCX parsers: `cwi_etl/parsers.py`
- IR schema models: `cwi_etl/ir.py`
- Normalization transforms: `cwi_etl/normalizer.py`
- Conflict detection: `cwi_etl/conflicts.py`
- Reconciliation engine + approvals: `cwi_etl/reconciliation.py`
- Report artifact emission: `cwi_etl/report.py`
- Pipeline helpers: `cwi_etl/pipeline.py`
- Operator memory correlation pipeline: `cwi_etl/operator_memory.py`
- Consent ingestion pipeline: `cwi_etl/consent_ingest.py`
- GraphRAG projection: `cwi_etl/graph_rag.py` (set `CWI_GRAPH_RAG_ENTITY_EXTRACTION=true` to enable extraction hooks)
- Graph ingestion client: `cwi_etl/graph_store.py` (requires `psycopg[binary]`)

## Graph DB configuration
Graph ingestion uses Postgres + Apache AGE. Configure with:
- `CWI_GRAPH_DB_DSN` (required, e.g. `postgresql://user:pass@host:5432/db`)
- `CWI_GRAPH_DB_NAME` (default: `cwi_graph`)
- `CWI_GRAPH_DB_CREATE_GRAPH` (default: true)
- `CWI_GRAPH_DB_BATCH_SIZE` (default: 100)

## GraphRAG feature flags
- `CWI_GRAPH_RAG_ENTITY_EXTRACTION` (default: false) enables NER/relationship extraction hooks

## Consent store configuration
Operator memory ingestion resolves consent from a SQLite consent store when callers do not supply consent:
- `CWI_CONSENT_DB_PATH` (required to auto-resolve consent)
- `CWI_CONSENT_DB_TABLE` (default: `operator_consent`)
- `CWI_CONSENT_DB_ENSURE_SCHEMA` (default: true)

Remote consent can be used instead of SQLite when configured:
- `CWI_CONSENT_REMOTE_BASE_URL` (required to use remote consent)
- `CWI_CONSENT_REMOTE_PATH` (default: `/api/consent/latest`)
- `CWI_CONSENT_REMOTE_TOKEN` (optional bearer token)
- `CWI_CONSENT_REMOTE_TIMEOUT_SECONDS` (default: 5)

Remote consent responses must include either top-level `granted`/`granted_utc` fields or a `consent` object
with those fields. Optional fields: `source`, `consent_granted`, `consent_granted_utc`.

## Consent ingestion
The consent ingestion job maps source payloads into the consent store. Supported source types:
- `operator_memory_ingest` (expects `tenant_id`, `operator_id`, and `consent.granted`/`consent.granted_utc`)
