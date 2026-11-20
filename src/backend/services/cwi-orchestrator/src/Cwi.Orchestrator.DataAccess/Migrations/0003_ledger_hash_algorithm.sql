ALTER TABLE ledger_events
  ADD COLUMN IF NOT EXISTS hash_algorithm text NOT NULL DEFAULT 'sha256-v1';

ALTER TABLE ledger_outbox
  ADD COLUMN IF NOT EXISTS hash_algorithm text NOT NULL DEFAULT 'sha256-v1';
