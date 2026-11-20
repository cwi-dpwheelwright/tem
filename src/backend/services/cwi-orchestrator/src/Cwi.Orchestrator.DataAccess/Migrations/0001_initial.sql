CREATE TABLE IF NOT EXISTS tenant_config (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL UNIQUE,
  config_json jsonb NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  status text NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS approval_tickets (
  id uuid PRIMARY KEY,
  ticket_id text NOT NULL UNIQUE,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  risk text NOT NULL,
  reason text NOT NULL,
  status text NOT NULL,
  idempotency_key text NOT NULL UNIQUE,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ledger_events (
  id uuid PRIMARY KEY,
  trace_id text NOT NULL,
  event_type text NOT NULL,
  payload_json jsonb NOT NULL,
  prev_hash text NOT NULL,
  hash text NOT NULL,
  signature text NULL,
  signature_algorithm text NULL,
  key_id text NULL,
  created_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ledger_outbox (
  id uuid PRIMARY KEY,
  trace_id text NOT NULL,
  event_type text NOT NULL,
  payload_json jsonb NOT NULL,
  prev_hash text NOT NULL,
  hash text NOT NULL,
  signature text NULL,
  signature_algorithm text NULL,
  key_id text NULL,
  created_utc timestamptz NOT NULL,
  published_utc timestamptz NULL
);

CREATE TABLE IF NOT EXISTS telemetry_windows (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  window_start_utc timestamptz NOT NULL,
  window_end_utc timestamptz NOT NULL,
  payload_json jsonb NOT NULL,
  created_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS connector_runs (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  connector_kind text NOT NULL,
  status text NOT NULL,
  cursor text NOT NULL,
  started_utc timestamptz NOT NULL,
  completed_utc timestamptz NULL
);
