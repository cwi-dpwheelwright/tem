CREATE TABLE IF NOT EXISTS outcome_feedback (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "TraceId" text NOT NULL,
  "TicketId" text NULL,
  "ActionId" text NULL,
  "ActorId" text NOT NULL,
  "IdempotencyKey" text NOT NULL,
  "FeedbackType" text NOT NULL,
  "ConsentGranted" boolean NOT NULL DEFAULT false,
  "Notes" text NULL,
  "ConfidenceBefore" double precision NULL,
  "ConfidenceAfter" double precision NULL,
  "CreatedUtc" timestamptz NOT NULL
);

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback'
      AND column_name IN ('IdempotencyKey', 'idempotency_key')
  ) THEN
    ALTER TABLE outcome_feedback ADD COLUMN "IdempotencyKey" text NOT NULL DEFAULT '';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback'
      AND column_name IN ('ConsentGranted', 'consent_granted')
  ) THEN
    ALTER TABLE outcome_feedback ADD COLUMN "ConsentGranted" boolean NOT NULL DEFAULT false;
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'IdempotencyKey'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS outcome_feedback_idempotency_key_idx
      ON outcome_feedback ("IdempotencyKey");
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'idempotency_key'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS outcome_feedback_idempotency_key_idx
      ON outcome_feedback (idempotency_key);
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_tenant_id_idx ON outcome_feedback ("TenantId");
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_tenant_id_idx ON outcome_feedback (tenant_id);
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'TraceId'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_trace_id_idx ON outcome_feedback ("TraceId");
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'trace_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_trace_id_idx ON outcome_feedback (trace_id);
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'TicketId'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_ticket_id_idx ON outcome_feedback ("TicketId");
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'ticket_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_ticket_id_idx ON outcome_feedback (ticket_id);
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'ActionId'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_action_id_idx ON outcome_feedback ("ActionId");
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'outcome_feedback' AND column_name = 'action_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS outcome_feedback_action_id_idx ON outcome_feedback (action_id);
  END IF;
END $$;
