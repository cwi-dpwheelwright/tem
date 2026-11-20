ALTER TABLE coordination_members
  ADD COLUMN IF NOT EXISTS tenant_id text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS owner_user_id text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS device_class text NULL,
  ADD COLUMN IF NOT EXISTS tailscale_ip text NULL,
  ADD COLUMN IF NOT EXISTS watcher_node_id text NULL,
  ADD COLUMN IF NOT EXISTS capabilities_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  ADD COLUMN IF NOT EXISTS pack_versions_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  ADD COLUMN IF NOT EXISTS policy_profile_version text NOT NULL DEFAULT '1.0',
  ADD COLUMN IF NOT EXISTS health_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  ADD COLUMN IF NOT EXISTS last_seen_utc timestamptz NULL;

CREATE INDEX IF NOT EXISTS coordination_members_tenant_owner_idx
  ON coordination_members (tenant_id, owner_user_id);

CREATE TABLE IF NOT EXISTS coordination_node_work_items (
  id uuid PRIMARY KEY,
  work_id text NOT NULL UNIQUE,
  request_id text NULL,
  trace_id text NULL,
  node_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NULL,
  task_type text NOT NULL,
  policy_scope text NULL,
  payload_json jsonb NOT NULL,
  work_token text NULL,
  idempotency_key text NULL UNIQUE,
  status text NOT NULL,
  created_utc timestamptz NOT NULL,
  available_utc timestamptz NOT NULL,
  expires_utc timestamptz NULL,
  lease_id text NULL,
  lease_expires_utc timestamptz NULL,
  lease_count integer NOT NULL DEFAULT 0,
  result_json jsonb NULL,
  completed_utc timestamptz NULL,
  updated_utc timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS coordination_node_work_items_node_status_available_idx
  ON coordination_node_work_items (node_id, status, available_utc);
