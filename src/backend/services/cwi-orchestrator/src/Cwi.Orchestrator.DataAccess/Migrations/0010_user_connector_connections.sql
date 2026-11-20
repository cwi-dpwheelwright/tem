CREATE TABLE IF NOT EXISTS user_connector_connections (
  id uuid PRIMARY KEY,
  connection_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  connector_type text NOT NULL,
  display_name text NOT NULL,
  status text NOT NULL,
  auth_type text NOT NULL,
  credential_ref text NULL,
  scopes_json jsonb NOT NULL DEFAULT '[]'::jsonb,
  config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  sync_enabled boolean NOT NULL DEFAULT false,
  sync_frequency_minutes integer NOT NULL DEFAULT 360,
  visibility_scope text NOT NULL DEFAULT 'private',
  local_only boolean NOT NULL DEFAULT false,
  last_sync_status text NULL,
  last_sync_utc timestamptz NULL,
  last_error text NULL,
  provenance_id text NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL,
  disconnected_utc timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS user_connector_connections_tenant_user_connection_idx
  ON user_connector_connections (tenant_id, user_id, connection_id);

CREATE INDEX IF NOT EXISTS user_connector_connections_tenant_user_type_idx
  ON user_connector_connections (tenant_id, user_id, connector_type);

CREATE INDEX IF NOT EXISTS user_connector_connections_status_idx
  ON user_connector_connections (status);
