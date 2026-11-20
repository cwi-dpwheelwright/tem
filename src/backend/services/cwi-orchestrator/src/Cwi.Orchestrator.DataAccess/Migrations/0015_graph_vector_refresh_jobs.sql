CREATE TABLE IF NOT EXISTS graph_vector_refresh_jobs (
    id uuid PRIMARY KEY,
    job_id text NOT NULL,
    tenant_id text NOT NULL,
    user_id_hash text NULL,
    node_id text NULL,
    graph_candidate_id text NOT NULL,
    recursive_event_id text NOT NULL,
    trace_id text NOT NULL,
    fact_key text NOT NULL,
    refresh_reason text NOT NULL,
    refresh_status text NOT NULL,
    affected_vector_scope text NOT NULL,
    source_hash text NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    last_error text NULL,
    created_utc timestamptz NOT NULL,
    updated_utc timestamptz NOT NULL,
    completed_utc timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_graph_vector_refresh_jobs_job_id
    ON graph_vector_refresh_jobs (job_id);

CREATE UNIQUE INDEX IF NOT EXISTS ix_graph_vector_refresh_jobs_graph_candidate_id
    ON graph_vector_refresh_jobs (graph_candidate_id);

CREATE INDEX IF NOT EXISTS ix_graph_vector_refresh_jobs_status
    ON graph_vector_refresh_jobs (tenant_id, refresh_status, updated_utc);

CREATE INDEX IF NOT EXISTS ix_graph_vector_refresh_jobs_fact
    ON graph_vector_refresh_jobs (tenant_id, fact_key, refresh_status);

CREATE INDEX IF NOT EXISTS ix_graph_vector_refresh_jobs_trace
    ON graph_vector_refresh_jobs (trace_id);
