CREATE TABLE IF NOT EXISTS governed_memory (
  id uuid PRIMARY KEY,
  memory_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NULL,
  node_id text NULL,
  memory_scope text NOT NULL,
  memory_type text NOT NULL,
  memory_text text NOT NULL,
  confidence real NOT NULL,
  sensitivity_level text NOT NULL,
  retention_policy text NOT NULL,
  access_scope text NOT NULL,
  local_only boolean NOT NULL,
  sync_allowed boolean NOT NULL,
  content_sha256 text NOT NULL,
  provenance_id text NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS governed_memory_tenant_memory_idx
  ON governed_memory (tenant_id, memory_id);

CREATE INDEX IF NOT EXISTS governed_memory_tenant_user_scope_updated_idx
  ON governed_memory (tenant_id, user_id, memory_scope, updated_utc DESC);

CREATE INDEX IF NOT EXISTS governed_memory_tenant_node_local_idx
  ON governed_memory (tenant_id, node_id, local_only);

CREATE TABLE IF NOT EXISTS conversation_summaries (
  id uuid PRIMARY KEY,
  summary_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  conversation_id text NOT NULL,
  summary_text text NOT NULL,
  covered_turn_ids_json jsonb NOT NULL,
  confidence real NOT NULL,
  content_sha256 text NOT NULL,
  provenance_id text NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS conversation_summaries_tenant_user_summary_idx
  ON conversation_summaries (tenant_id, user_id, summary_id);

CREATE INDEX IF NOT EXISTS conversation_summaries_tenant_user_conversation_updated_idx
  ON conversation_summaries (tenant_id, user_id, conversation_id, updated_utc DESC);

CREATE TABLE IF NOT EXISTS source_memory (
  id uuid PRIMARY KEY,
  source_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NULL,
  conversation_id text NULL,
  node_id text NULL,
  filename text NOT NULL,
  source_type text NOT NULL,
  storage_location text NOT NULL,
  extraction_method text NOT NULL,
  local_only boolean NOT NULL,
  sync_allowed boolean NOT NULL,
  content_sha256 text NOT NULL,
  provenance_id text NULL,
  created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS source_memory_tenant_source_idx
  ON source_memory (tenant_id, source_id);

CREATE INDEX IF NOT EXISTS source_memory_tenant_user_created_idx
  ON source_memory (tenant_id, user_id, created_utc DESC);

CREATE INDEX IF NOT EXISTS source_memory_tenant_node_local_idx
  ON source_memory (tenant_id, node_id, local_only);

CREATE TABLE IF NOT EXISTS source_chunks (
  id uuid PRIMARY KEY,
  chunk_id text NOT NULL,
  source_id text NOT NULL,
  tenant_id text NOT NULL,
  user_id text NULL,
  conversation_id text NULL,
  node_id text NULL,
  chunk_text text NOT NULL,
  chunk_index integer NOT NULL,
  heading_context text NULL,
  local_only boolean NOT NULL,
  content_sha256 text NOT NULL,
  embedding_id text NULL,
  created_utc timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS source_chunks_tenant_source_chunk_idx
  ON source_chunks (tenant_id, source_id, chunk_id);

CREATE INDEX IF NOT EXISTS source_chunks_tenant_user_created_idx
  ON source_chunks (tenant_id, user_id, created_utc DESC);

CREATE INDEX IF NOT EXISTS source_chunks_tenant_node_local_idx
  ON source_chunks (tenant_id, node_id, local_only);

ALTER TABLE ask_history
  ADD COLUMN IF NOT EXISTS conversation_id text NOT NULL DEFAULT 'default',
  ADD COLUMN IF NOT EXISTS turn_id text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS provenance_id text NULL,
  ADD COLUMN IF NOT EXISTS content_sha256 text NOT NULL DEFAULT '',
  ADD COLUMN IF NOT EXISTS summary_status text NOT NULL DEFAULT 'pending';

UPDATE ask_history
SET turn_id = replace(id::text, '-', '')
WHERE turn_id = '';

UPDATE ask_history
SET provenance_id = trace_id
WHERE provenance_id IS NULL AND trace_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ask_history_tenant_user_conversation_created_idx
  ON ask_history (tenant_id, user_id, conversation_id, created_utc DESC);
