CREATE TABLE IF NOT EXISTS coordination_members (
  id uuid PRIMARY KEY,
  node_id text NOT NULL UNIQUE,
  certificate_fingerprint text NOT NULL,
  status text NOT NULL,
  joined_utc timestamptz NOT NULL,
  revoked_utc timestamptz NULL
);

CREATE TABLE IF NOT EXISTS coordination_rounds (
  id uuid PRIMARY KEY,
  name text NOT NULL,
  status text NOT NULL,
  created_utc timestamptz NOT NULL,
  closed_utc timestamptz NULL
);

CREATE TABLE IF NOT EXISTS coordination_ledger_events (
  id uuid PRIMARY KEY,
  event_type text NOT NULL,
  payload_json text NOT NULL,
  prev_hash text NOT NULL,
  hash text NOT NULL,
  created_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS coordination_ledger_events_created_utc_idx
  ON coordination_ledger_events (created_utc);

CREATE TABLE IF NOT EXISTS coordination_link_states (
  node_id text NOT NULL,
  interface_kind text NOT NULL,
  signal_quality double precision NOT NULL,
  latency_ms integer NOT NULL,
  jitter_ms double precision NULL,
  packet_loss double precision NOT NULL,
  bandwidth_kbps integer NOT NULL,
  throughput_kbps integer NOT NULL,
  rssi_dbm double precision NULL,
  snr_db double precision NULL,
  status text NOT NULL,
  updated_utc timestamptz NOT NULL,
  PRIMARY KEY (node_id, interface_kind)
);

CREATE INDEX IF NOT EXISTS coordination_link_states_node_id_idx
  ON coordination_link_states (node_id);

CREATE TABLE IF NOT EXISTS coordination_mesh_events (
  id uuid PRIMARY KEY,
  node_id text NOT NULL,
  interface_kind text NOT NULL,
  event_type text NOT NULL,
  reason text NOT NULL,
  created_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS coordination_mesh_events_created_utc_idx
  ON coordination_mesh_events (created_utc);

CREATE TABLE IF NOT EXISTS coordination_degradation_events (
  id uuid PRIMARY KEY,
  event_id text NOT NULL UNIQUE,
  node_id text NOT NULL,
  observed_utc timestamptz NOT NULL,
  severity double precision NOT NULL,
  details jsonb NOT NULL,
  provenance jsonb NULL,
  received_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS coordination_degradation_events_received_utc_idx
  ON coordination_degradation_events (received_utc);

CREATE TABLE IF NOT EXISTS coordination_backhaul_policy (
  id uuid PRIMARY KEY,
  require_authenticated_channel boolean NOT NULL,
  allowed_interfaces jsonb NOT NULL,
  denied_interfaces jsonb NOT NULL,
  revoked_peers jsonb NOT NULL,
  policy_version text NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS coordination_survival_mode (
  id uuid PRIMARY KEY,
  enabled boolean NOT NULL,
  reason text NULL,
  updated_utc timestamptz NOT NULL
);
