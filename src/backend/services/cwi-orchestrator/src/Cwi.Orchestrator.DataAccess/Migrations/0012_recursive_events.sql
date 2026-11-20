CREATE TABLE IF NOT EXISTS recursive_events (
  id uuid PRIMARY KEY,
  event_id text NOT NULL,
  schema_version text NOT NULL,
  event_type text NOT NULL,
  tenant_id text NOT NULL,
  scope_type text NOT NULL,
  user_id text NULL,
  user_id_hash text NULL,
  node_id text NULL,
  source_component text NOT NULL,
  source_type text NOT NULL,
  source_id text NULL,
  source_node_id text NULL,
  confidence double precision NOT NULL,
  trace_id text NOT NULL,
  policy_status text NOT NULL,
  temporal_state text NOT NULL,
  timestamp_utc timestamptz NOT NULL,
  payload_json jsonb NOT NULL,
  provenance_json jsonb NOT NULL,
  event_json jsonb NOT NULL,
  source_hash text NOT NULL,
  created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS recursive_events_event_id_idx
  ON recursive_events (event_id);

CREATE INDEX IF NOT EXISTS recursive_events_tenant_type_timestamp_idx
  ON recursive_events (tenant_id, event_type, timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS recursive_events_tenant_temporal_timestamp_idx
  ON recursive_events (tenant_id, temporal_state, timestamp_utc DESC);

CREATE INDEX IF NOT EXISTS recursive_events_trace_idx
  ON recursive_events (trace_id);

CREATE TABLE IF NOT EXISTS recursive_event_quarantine (
  id uuid PRIMARY KEY,
  quarantine_id text NOT NULL,
  event_id text NULL,
  tenant_id text NULL,
  trace_id text NOT NULL,
  reason text NOT NULL,
  status text NOT NULL,
  payload_json jsonb NOT NULL,
  validation_error text NULL,
  created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS recursive_event_quarantine_id_idx
  ON recursive_event_quarantine (quarantine_id);

CREATE INDEX IF NOT EXISTS recursive_event_quarantine_event_id_idx
  ON recursive_event_quarantine (event_id);

CREATE INDEX IF NOT EXISTS recursive_event_quarantine_tenant_idx
  ON recursive_event_quarantine (tenant_id);

CREATE INDEX IF NOT EXISTS recursive_event_quarantine_trace_idx
  ON recursive_event_quarantine (trace_id);
