select jsonb_pretty(jsonb_build_object(
  'generated_utc', to_char((now() at time zone 'utc'), 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
  'counts', jsonb_build_object(
    'ask_history', (select count(*) from ask_history),
    'conversation_summaries', (select count(*) from conversation_summaries),
    'source_memory', (select count(*) from source_memory),
    'source_chunks', (select count(*) from source_chunks),
    'governed_memory', (select count(*) from governed_memory),
    'memory_fact_index', (select count(*) from memory_fact_index),
    'normalized_connector_records', (select count(*) from normalized_connector_records),
    'connector_ingestion_receipts', (select count(*) from connector_ingestion_receipts),
    'approval_tickets_total', (select count(*) from approval_tickets),
    'approval_tickets_pending', (select count(*) from approval_tickets where lower("Status") in ('pending','open','requires_approval','waiting','queued')),
    'connector_runs_total', (select count(*) from connector_runs),
    'connector_runs_active', (select count(*) from connector_runs where lower("Status") in ('pending','running','queued','in_progress')),
    'ledger_events_preserved', (select count(*) from ledger_events),
    'sessions_preserved', (select count(*) from sessions),
    'tenant_config_preserved', (select count(*) from tenant_config),
    'user_connector_connections_preserved', (select count(*) from user_connector_connections)
  )
));