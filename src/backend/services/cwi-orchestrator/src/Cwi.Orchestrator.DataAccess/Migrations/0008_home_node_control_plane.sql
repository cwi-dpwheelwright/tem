CREATE TABLE IF NOT EXISTS home_nodes (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  node_id text NOT NULL,
  owner_user_id text NOT NULL,
  device_class text NULL,
  tailscale_ip text NULL,
  watcher_node_id text NULL,
  capabilities_json jsonb NOT NULL,
  pack_versions_json jsonb NOT NULL,
  policy_profile_version text NOT NULL,
  status text NOT NULL,
  last_heartbeat_utc timestamptz NULL,
  assigned_utc timestamptz NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS home_nodes_tenant_node_idx
  ON home_nodes (tenant_id, node_id);

CREATE INDEX IF NOT EXISTS home_nodes_tenant_owner_idx
  ON home_nodes (tenant_id, owner_user_id);

CREATE INDEX IF NOT EXISTS home_nodes_status_idx
  ON home_nodes (status);

CREATE TABLE IF NOT EXISTS home_node_assignments (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  primary_home_node_id text NOT NULL,
  watcher_node_id text NULL,
  policy_scope_id text NULL,
  local_only boolean NOT NULL,
  remote_fallback_allowed boolean NOT NULL,
  status text NOT NULL,
  assigned_utc timestamptz NOT NULL,
  revoked_utc timestamptz NULL,
  updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS home_node_assignments_tenant_user_idx
  ON home_node_assignments (tenant_id, user_id);

CREATE INDEX IF NOT EXISTS home_node_assignments_tenant_primary_idx
  ON home_node_assignments (tenant_id, primary_home_node_id);

CREATE INDEX IF NOT EXISTS home_node_assignments_status_idx
  ON home_node_assignments (status);

CREATE TABLE IF NOT EXISTS home_node_policy_scopes (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  policy_scope_id text NOT NULL,
  rules_json jsonb NOT NULL,
  version text NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS home_node_policy_scopes_tenant_scope_idx
  ON home_node_policy_scopes (tenant_id, policy_scope_id);
