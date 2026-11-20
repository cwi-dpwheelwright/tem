CREATE TABLE IF NOT EXISTS sustainability_request_records (
    id uuid PRIMARY KEY,
    request_id text NOT NULL,
    trace_id text NOT NULL,
    timestamp_utc timestamptz NOT NULL,
    tenant_id text NOT NULL,
    user_id_hash text NOT NULL,
    user_role text NOT NULL,
    node_id text NOT NULL,
    node_name text NOT NULL,
    node_group text NOT NULL,
    node_role text NOT NULL,
    duration_seconds double precision NOT NULL,
    energy_wh double precision NULL,
    energy_value_type text NOT NULL,
    cost_usd double precision NULL,
    cost_value_type text NOT NULL,
    water_liters double precision NULL,
    water_value_type text NOT NULL DEFAULT 'unknown',
    baseline_model_version text NOT NULL DEFAULT 'none',
    cloud_baseline_energy_wh double precision NULL,
    cloud_baseline_energy_value_type text NOT NULL DEFAULT 'unknown',
    cloud_baseline_cost_usd double precision NULL,
    cloud_baseline_cost_value_type text NOT NULL DEFAULT 'unknown',
    cloud_baseline_water_liters double precision NULL,
    cloud_baseline_water_value_type text NOT NULL DEFAULT 'unknown',
    energy_saved_wh double precision NULL,
    cost_saved_usd double precision NULL,
    water_saved_liters double precision NULL,
    savings_value_type text NOT NULL DEFAULT 'unknown',
    status text NOT NULL,
    model_or_runtime text NOT NULL,
    source text NOT NULL,
    created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_sustainability_request_records_request_id
    ON sustainability_request_records (request_id);

CREATE INDEX IF NOT EXISTS ix_sustainability_request_records_trace
    ON sustainability_request_records (trace_id);

CREATE INDEX IF NOT EXISTS ix_sustainability_request_records_tenant_time
    ON sustainability_request_records (tenant_id, timestamp_utc);

CREATE INDEX IF NOT EXISTS ix_sustainability_request_records_user_time
    ON sustainability_request_records (tenant_id, user_id_hash, timestamp_utc);

CREATE INDEX IF NOT EXISTS ix_sustainability_request_records_node_time
    ON sustainability_request_records (tenant_id, node_id, timestamp_utc);
