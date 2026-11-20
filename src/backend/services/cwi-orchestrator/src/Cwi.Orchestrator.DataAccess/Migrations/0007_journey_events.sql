ALTER TABLE ask_history
  ADD COLUMN IF NOT EXISTS request_id text;

CREATE INDEX IF NOT EXISTS ask_history_request_id_idx
  ON ask_history (request_id);

CREATE TABLE IF NOT EXISTS journey_events (
  id BIGSERIAL PRIMARY KEY,
  event_id text NOT NULL,
  stream_id text NOT NULL,
  request_id text NOT NULL,
  trace_id text NULL,
  service text NOT NULL,
  component text NOT NULL,
  phase text NOT NULL,
  direction text NOT NULL,
  from_component text NULL,
  to_component text NULL,
  tenant_id text NULL,
  user_id text NULL,
  pack_name text NULL,
  method text NULL,
  path text NULL,
  status_code integer NULL,
  duration_ms bigint NULL,
  message text NULL,
  metadata_json jsonb NULL,
  created_utc timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS journey_events_event_id_idx
  ON journey_events (event_id);

CREATE UNIQUE INDEX IF NOT EXISTS journey_events_stream_id_idx
  ON journey_events (stream_id);

CREATE INDEX IF NOT EXISTS journey_events_request_created_idx
  ON journey_events (request_id, created_utc, id);

CREATE INDEX IF NOT EXISTS journey_events_trace_created_idx
  ON journey_events (trace_id, created_utc, id);
