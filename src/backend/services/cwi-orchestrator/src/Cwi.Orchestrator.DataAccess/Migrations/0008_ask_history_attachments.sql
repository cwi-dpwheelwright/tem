ALTER TABLE ask_history
  ADD COLUMN IF NOT EXISTS attachments_json jsonb NULL;
