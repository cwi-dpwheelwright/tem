# CWI Observability

This directory contains Prometheus and Grafana assets for CWI runtime visibility.

## Sustainability Metrics

The sustainability dashboard uses two data paths:

- Prometheus for aggregate live counters:
  - `cwi_requests_total`
  - `cwi_request_energy_wh_total`
  - `cwi_request_cost_usd_total`
  - `cwi_request_water_liters_total`
  - `cwi_cloud_baseline_energy_wh_total`
  - `cwi_cloud_baseline_cost_usd_total`
  - `cwi_cloud_baseline_water_liters_total`
  - `cwi_energy_saved_wh_total`
  - `cwi_cost_saved_usd_total`
  - `cwi_water_saved_liters_total`
  - `cwi_request_duration_seconds_sum`
  - `cwi_request_duration_seconds_count`
- The orchestrator ledger database for exact per-request and per-user reporting.

Do not add `user_id_hash` as a Prometheus label. User-level reporting belongs in
the ledger/database because it has high cardinality and exact audit semantics.

The dashboard expects these Grafana datasource variables:

- `${DS_PROMETHEUS}` for Prometheus.
- `${DS_POSTGRES}` for the orchestrator database.

`${DS_POSTGRES}` must point at the orchestrator Postgres database, not the AGE
graph database. In the MCP core deployment this is exposed by
`OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` using
`ORCHESTRATOR_DB_HOST_PORT` (default `15433`). The checked-in dashboard SQL reads
from `sustainability_request_records`; it does not read sustainability data from
`journey_events`.

Migration `0017_sustainability_request_records_backfill.sql` can backfill sparse
request-record rows from the latest `journey_events` sustainability metadata for
the same `request_id`. It also adds node watt/source attribution columns used by
new orchestrator emissions.

Cloud baseline comparison and water-savings panels read only from configured
orchestrator estimate inputs. The MCP core compose template includes seeded
estimate values so Grafana panels do not stay null during local operations. These
values are labeled as estimates in the request ledger and should be replaced in
`.env` with site-approved values before treating reports as operational
accounting.

Baseline configuration keys:

- `CWI_SUSTAINABILITY_ESTIMATED_NODE_WATTS`
- `CWI_SUSTAINABILITY_COST_USD_PER_KWH`
- `CWI_SUSTAINABILITY_ESTIMATED_WATER_LITERS_PER_REQUEST`
- `CWI_SUSTAINABILITY_CLOUD_BASELINE_VERSION`
- `CWI_SUSTAINABILITY_CLOUD_ENERGY_WH_PER_REQUEST`
- `CWI_SUSTAINABILITY_CLOUD_COST_USD_PER_REQUEST`
- `CWI_SUSTAINABILITY_CLOUD_WATER_LITERS_PER_REQUEST`

All configured CWI-side and cloud-baseline values are labeled as estimates in
the request ledger. Per-user savings reports should read from the ledger fields,
not from Prometheus labels.

Seeded compose defaults:

- local node draw: `180` watts
- local energy price: `0.12` USD/kWh
- local water estimate: `0.005` liters/request
- cloud baseline model: `cwi-sustainability-seeded-estimates.v1`
- cloud baseline energy: `3` Wh/request
- cloud baseline cost: `0.001` USD/request
- cloud baseline water: `0.02` liters/request

If a container is started without those env vars, the orchestrator uses a
conservative code fallback of `60` watts plus the same cost, water, and cloud
baseline defaults so request records do not stay null.
