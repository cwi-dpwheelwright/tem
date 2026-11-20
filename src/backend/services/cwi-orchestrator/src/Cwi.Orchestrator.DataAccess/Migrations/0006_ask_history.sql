CREATE TABLE IF NOT EXISTS ask_history (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  question text NOT NULL,
  answer text NOT NULL,
  trace_id text NULL,
  pack_name text NULL,
  pack_hash text NULL,
  trace_json jsonb NULL,
  created_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS ask_history_tenant_user_created_idx
  ON ask_history (tenant_id, user_id, created_utc DESC);
