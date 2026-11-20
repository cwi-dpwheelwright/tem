CREATE TABLE IF NOT EXISTS validated_cache_records (
    id uuid PRIMARY KEY,
    cache_key text NOT NULL,
    tenant_id text NOT NULL,
    scope text NOT NULL,
    user_id_hash text NULL,
    source_proof_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    trace_id text NOT NULL,
    fresh_until_utc timestamptz NOT NULL,
    policy_mode text NOT NULL,
    policy_version text NULL,
    pack_hash text NOT NULL,
    validation_status text NOT NULL,
    critic_status text NULL,
    payload_hash text NULL,
    created_utc timestamptz NOT NULL,
    updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_validated_cache_records_tenant_cache_key
    ON validated_cache_records (tenant_id, cache_key);

CREATE INDEX IF NOT EXISTS ix_validated_cache_records_scope
    ON validated_cache_records (tenant_id, scope, user_id_hash);

CREATE INDEX IF NOT EXISTS ix_validated_cache_records_trace
    ON validated_cache_records (trace_id);

CREATE INDEX IF NOT EXISTS ix_validated_cache_records_fresh_until
    ON validated_cache_records (fresh_until_utc);
