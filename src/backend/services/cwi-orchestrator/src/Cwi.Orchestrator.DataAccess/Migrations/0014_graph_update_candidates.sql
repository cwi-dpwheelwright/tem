CREATE TABLE IF NOT EXISTS graph_update_candidates (
    id uuid PRIMARY KEY,
    candidate_id text NOT NULL,
    tenant_id text NOT NULL,
    user_id_hash text NULL,
    node_id text NULL,
    recursive_event_id text NOT NULL,
    trace_id text NOT NULL,
    candidate_state text NOT NULL,
    temporal_state text NOT NULL,
    subject text NOT NULL,
    relation text NOT NULL,
    object text NOT NULL,
    fact_key text NOT NULL,
    confidence double precision NOT NULL,
    provenance_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    source_component text NOT NULL,
    source_hash text NOT NULL,
    observed_utc timestamptz NOT NULL,
    created_utc timestamptz NOT NULL,
    updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_graph_update_candidates_candidate_id
    ON graph_update_candidates (candidate_id);

CREATE UNIQUE INDEX IF NOT EXISTS ix_graph_update_candidates_recursive_event_id
    ON graph_update_candidates (recursive_event_id);

CREATE INDEX IF NOT EXISTS ix_graph_update_candidates_state
    ON graph_update_candidates (tenant_id, candidate_state, temporal_state);

CREATE INDEX IF NOT EXISTS ix_graph_update_candidates_fact
    ON graph_update_candidates (tenant_id, fact_key, temporal_state);

CREATE INDEX IF NOT EXISTS ix_graph_update_candidates_trace
    ON graph_update_candidates (trace_id);
