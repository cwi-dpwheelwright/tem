CREATE TABLE IF NOT EXISTS break_glass_tokens (
  id uuid PRIMARY KEY,
  token_id text NOT NULL UNIQUE,
  tenant_id text NOT NULL,
  user_id text NOT NULL,
  role text NOT NULL,
  scope text NOT NULL,
  reason text NOT NULL,
  expires_utc timestamptz NOT NULL,
  created_utc timestamptz NOT NULL,
  revoked_utc timestamptz NULL
);

CREATE TABLE IF NOT EXISTS pack_activation_states (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  pack_name text NOT NULL,
  pack_hash text NOT NULL,
  status text NOT NULL,
  activated_by text NOT NULL,
  rolled_back_by text NOT NULL,
  rollback_reason text NOT NULL,
  activated_utc timestamptz NOT NULL,
  rolled_back_utc timestamptz NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS canonical_records (
  id uuid PRIMARY KEY,
  record_id text NOT NULL,
  canonical_json jsonb NOT NULL,
  sources_json jsonb NOT NULL,
  conflicts_json jsonb NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ownership_transfer_requests (
  id uuid PRIMARY KEY,
  request_id text NOT NULL UNIQUE,
  tenant_id text NOT NULL,
  from_user_id text NOT NULL,
  to_user_id text NOT NULL,
  transfer_type text NOT NULL,
  reason text NULL,
  status text NOT NULL,
  required_approvals integer NOT NULL,
  approvers_json jsonb NOT NULL,
  approved_by_json jsonb NOT NULL,
  approval_ticket_id text NOT NULL,
  idempotency_key text NOT NULL UNIQUE,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL,
  completed_utc timestamptz NULL
);

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_tenant_id_idx ON break_glass_tokens (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_tenant_id_idx ON break_glass_tokens ("TenantId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'user_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_user_id_idx ON break_glass_tokens (user_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'UserId'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_user_id_idx ON break_glass_tokens ("UserId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'expires_utc'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_expires_utc_idx ON break_glass_tokens (expires_utc);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'break_glass_tokens' AND column_name = 'ExpiresUtc'
  ) THEN
    CREATE INDEX IF NOT EXISTS break_glass_tokens_expires_utc_idx ON break_glass_tokens ("ExpiresUtc");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'tenant_id'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'pack_name'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS pack_activation_states_tenant_pack_idx ON pack_activation_states (tenant_id, pack_name);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'TenantId'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'PackName'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS pack_activation_states_tenant_pack_idx ON pack_activation_states ("TenantId", "PackName");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'status'
  ) THEN
    CREATE INDEX IF NOT EXISTS pack_activation_states_status_idx ON pack_activation_states (status);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'pack_activation_states' AND column_name = 'Status'
  ) THEN
    CREATE INDEX IF NOT EXISTS pack_activation_states_status_idx ON pack_activation_states ("Status");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'canonical_records' AND column_name = 'record_id'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS canonical_records_record_id_idx ON canonical_records (record_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'canonical_records' AND column_name = 'RecordId'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS canonical_records_record_id_idx ON canonical_records ("RecordId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'approval_ticket_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_approval_ticket_id_idx
      ON ownership_transfer_requests (approval_ticket_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'ApprovalTicketId'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_approval_ticket_id_idx
      ON ownership_transfer_requests ("ApprovalTicketId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_tenant_id_idx
      ON ownership_transfer_requests (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_tenant_id_idx
      ON ownership_transfer_requests ("TenantId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'status'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_status_idx
      ON ownership_transfer_requests (status);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_transfer_requests' AND column_name = 'Status'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_transfer_requests_status_idx
      ON ownership_transfer_requests ("Status");
  END IF;
END $$;
