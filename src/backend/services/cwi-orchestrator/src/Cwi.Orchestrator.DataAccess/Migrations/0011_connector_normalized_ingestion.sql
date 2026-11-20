CREATE TABLE IF NOT EXISTS normalized_connector_records (
  id uuid PRIMARY KEY,
  record_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  account_id text NOT NULL,
  connector_type text NOT NULL,
  source_system text NOT NULL,
  source_record_id text NOT NULL,
  source_thread_id text NULL,
  source_url text NULL,
  event_time timestamptz NOT NULL,
  ingested_at timestamptz NOT NULL,
  title text NOT NULL,
  body text NOT NULL,
  raw_excerpt text NULL,
  life_work_scope text NOT NULL,
  sensitivity_label text NOT NULL,
  confidence double precision NOT NULL,
  source_sha256 text NOT NULL,
  record_json jsonb NOT NULL,
  provenance_json jsonb NOT NULL,
  sync_state_json jsonb NOT NULL,
  is_deleted boolean NOT NULL,
  is_update boolean NOT NULL,
  stale boolean NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL,
  last_seen_utc timestamptz NOT NULL,
  replay_count integer NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX IF NOT EXISTS normalized_connector_records_tenant_user_record_idx
  ON normalized_connector_records (tenant_id, user_id, record_id);

CREATE INDEX IF NOT EXISTS normalized_connector_records_tenant_user_type_event_idx
  ON normalized_connector_records (tenant_id, user_id, connector_type, event_time DESC);

CREATE INDEX IF NOT EXISTS normalized_connector_records_tenant_user_scope_sensitivity_idx
  ON normalized_connector_records (tenant_id, user_id, life_work_scope, sensitivity_label);

CREATE TABLE IF NOT EXISTS connector_ingestion_receipts (
  id uuid PRIMARY KEY,
  receipt_id text NOT NULL,
  record_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  connector_type text NOT NULL,
  status text NOT NULL,
  memory_ids_created_json jsonb NOT NULL,
  memory_ids_updated_json jsonb NOT NULL,
  graph_edges_created_json jsonb NOT NULL,
  error text NULL,
  created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS connector_ingestion_receipts_receipt_idx
  ON connector_ingestion_receipts (receipt_id);

CREATE INDEX IF NOT EXISTS connector_ingestion_receipts_tenant_user_record_created_idx
  ON connector_ingestion_receipts (tenant_id, user_id, record_id, created_utc DESC);

CREATE INDEX IF NOT EXISTS connector_ingestion_receipts_tenant_user_status_created_idx
  ON connector_ingestion_receipts (tenant_id, user_id, status, created_utc DESC);

CREATE TABLE IF NOT EXISTS memory_fact_index (
  id uuid PRIMARY KEY,
  fact_key text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  life_work_scope text NOT NULL,
  memory_domain text NOT NULL,
  memory_type text NOT NULL,
  normalized_fact_text text NOT NULL,
  record_ids_json jsonb NOT NULL,
  confidence double precision NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL,
  last_seen_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS memory_fact_index_tenant_user_fact_idx
  ON memory_fact_index (tenant_id, user_id, fact_key);

CREATE INDEX IF NOT EXISTS memory_fact_index_tenant_user_scope_domain_type_idx
  ON memory_fact_index (tenant_id, user_id, life_work_scope, memory_domain, memory_type);
