\set ON_ERROR_STOP on
begin;
truncate table
  source_chunks,
  source_memory,
  governed_memory,
  conversation_summaries,
  ask_history,
  memory_fact_index,
  normalized_connector_records,
  connector_ingestion_receipts
restart identity;
delete from approval_tickets
where lower("Status") in ('pending','open','requires_approval','waiting','queued');
update connector_runs
set "Status" = 'cancelled', "CompletedUtc" = coalesce("CompletedUtc", now())
where lower("Status") in ('pending','running','queued','in_progress');
commit;