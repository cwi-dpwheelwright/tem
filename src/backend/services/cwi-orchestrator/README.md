# cwi-orchestrator

Minimal clean-architecture skeleton for the 10H orchestrator service.

## Build
```
dotnet restore cwi-orchestrator.sln --locked-mode
dotnet build cwi-orchestrator.sln -c Release
```

## Run (dev)
Set required env vars:
- REGISTRY
- CWI_ORCHESTRATOR_TAG
- ORCHESTRATOR_HTTP_PORT
- PACKS_BASE_URL
- ORCHESTRATOR_DB_CONNECTION
- VECTOR_SHARD_1_URL
- VECTOR_SHARD_2_URL
- VECTOR_EMBEDDING_DIMENSION
- MODEL_BASE_URL
- MODEL_NAME
- MODEL_TIMEOUT_SECONDS

Optional admin/dev controls:
- CWI_TRACE_QUERY_ENABLED (default: false; enables /api/dev/trace/{traceId}/spans)
- CWI_TRACE_QUERY_MAX_TRACES / CWI_TRACE_QUERY_MAX_SPANS_PER_TRACE / CWI_TRACE_QUERY_MAX_SPAN_AGE_SECONDS
- CWI_FAULT_INJECTION_ENABLED (default: false; enables /api/dev/fault)
- KILL_SWITCH_ADMIN_ENABLED (default: true; enables /api/admin/kill_switch)
- KILL_SWITCH_CODE (optional; if set, required for kill-switch toggle)
- KILL_SWITCH_TOKEN_REQUIRED (default: false)
- KILL_SWITCH_TOKEN_TTL_SECONDS / KILL_SWITCH_TOKEN_MAX_TTL_SECONDS
- DEFAULT_TENANT_ID (optional fallback for admin kill-switch)
- CWI_SUSTAINABILITY_ESTIMATED_NODE_WATTS / CWI_SUSTAINABILITY_COST_USD_PER_KWH /
  CWI_SUSTAINABILITY_ESTIMATED_WATER_LITERS_PER_REQUEST
- CWI_SUSTAINABILITY_CLOUD_BASELINE_VERSION /
  CWI_SUSTAINABILITY_CLOUD_ENERGY_WH_PER_REQUEST /
  CWI_SUSTAINABILITY_CLOUD_COST_USD_PER_REQUEST /
  CWI_SUSTAINABILITY_CLOUD_WATER_LITERS_PER_REQUEST

Then run:
```
dotnet run --project src/Cwi.Orchestrator.Api/Cwi.Orchestrator.Api.csproj
```

## Migrations
Apply SQL migrations in `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Migrations`
in order before starting the service. The deployment runner is
`OPS/cwi-ops/scripts/orchestrator-migrate.sh`. Latest:
`0017_sustainability_request_records_backfill.sql`.

## Tenant config (DB)
Tenant overrides live in the `tenant_config` table (`config_json`).
Succession approvals can be sourced from any of:
- `succession_approvers`: `["user-1", "user-2"]`
- `ownership_succession_approvers`: `["user-1"]`
- `succession.approvers`: `["user-1"]`
- `ownership.succession_approvers`: `["user-1"]`
- `ownership.succession.approvers`: `["user-1"]`

## Ingest vector fixtures
If you have a `vector_corpus.jsonl` fixture, ingest it into Qdrant:
```
python scripts/ingest-vector-fixtures.py --input <path> --collection cwi_corpus
```
The script will use `VECTOR_SHARD_1_URL`, `VECTOR_SHARD_2_URL`, and `VECTOR_EMBEDDING_DIMENSION` if available.
