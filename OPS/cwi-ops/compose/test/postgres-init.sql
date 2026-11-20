CREATE TABLE IF NOT EXISTS tenant_config (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL UNIQUE,
  "ConfigJson" jsonb NOT NULL,
  "CreatedUtc" timestamptz NOT NULL,
  "UpdatedUtc" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS sessions (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "UserId" text NOT NULL,
  "Status" text NOT NULL,
  "CreatedUtc" timestamptz NOT NULL,
  "UpdatedUtc" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS approval_tickets (
  "Id" uuid PRIMARY KEY,
  "TicketId" text NOT NULL UNIQUE,
  "TenantId" text NOT NULL,
  "UserId" text NOT NULL,
  "Risk" text NOT NULL,
  "Reason" text NOT NULL,
  "Status" text NOT NULL,
  "IdempotencyKey" text NOT NULL UNIQUE,
  "CreatedUtc" timestamptz NOT NULL,
  "UpdatedUtc" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ledger_events (
  "Id" uuid PRIMARY KEY,
  "TraceId" text NOT NULL,
  "EventType" text NOT NULL,
  "PayloadJson" text NOT NULL,
  "PrevHash" text NOT NULL,
  "Hash" text NOT NULL,
  "HashAlgorithm" text NOT NULL DEFAULT 'sha256-v1',
  "Signature" text NULL,
  "SignatureAlgorithm" text NULL,
  "KeyId" text NULL,
  "CreatedUtc" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ledger_outbox (
  "Id" uuid PRIMARY KEY,
  "TraceId" text NOT NULL,
  "EventType" text NOT NULL,
  "PayloadJson" text NOT NULL,
  "PrevHash" text NOT NULL,
  "Hash" text NOT NULL,
  "HashAlgorithm" text NOT NULL DEFAULT 'sha256-v1',
  "Signature" text NULL,
  "SignatureAlgorithm" text NULL,
  "KeyId" text NULL,
  "CreatedUtc" timestamptz NOT NULL,
  "PublishedUtc" timestamptz NULL
);

CREATE TABLE IF NOT EXISTS telemetry_windows (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "WindowStartUtc" timestamptz NOT NULL,
  "WindowEndUtc" timestamptz NOT NULL,
  "PayloadJson" jsonb NOT NULL,
  "CreatedUtc" timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS connector_runs (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "ConnectorKind" text NOT NULL,
  "Status" text NOT NULL,
  "Cursor" text NOT NULL,
  "StartedUtc" timestamptz NOT NULL,
  "CompletedUtc" timestamptz NULL
);

CREATE TABLE IF NOT EXISTS canonical_records (
  "Id" uuid PRIMARY KEY,
  "RecordId" text NOT NULL,
  "CanonicalJson" jsonb NOT NULL,
  "SourcesJson" jsonb NOT NULL,
  "ConflictsJson" jsonb NOT NULL,
  "UpdatedUtc" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS canonical_records_record_id_idx ON canonical_records ("RecordId");

CREATE TABLE IF NOT EXISTS ownership_transfer_requests (
  "Id" uuid PRIMARY KEY,
  "RequestId" text NOT NULL UNIQUE,
  "TenantId" text NOT NULL,
  "FromUserId" text NOT NULL,
  "ToUserId" text NOT NULL,
  "TransferType" text NOT NULL,
  "Reason" text NULL,
  "Status" text NOT NULL,
  "RequiredApprovals" integer NOT NULL,
  "ApproversJson" jsonb NOT NULL,
  "ApprovedByJson" jsonb NOT NULL,
  "ApprovalTicketId" text NOT NULL,
  "IdempotencyKey" text NOT NULL UNIQUE,
  "CreatedUtc" timestamptz NOT NULL,
  "UpdatedUtc" timestamptz NOT NULL,
  "CompletedUtc" timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ownership_transfer_requests_approval_ticket_id_idx ON ownership_transfer_requests ("ApprovalTicketId");
CREATE INDEX IF NOT EXISTS ownership_transfer_requests_tenant_id_idx ON ownership_transfer_requests ("TenantId");
CREATE INDEX IF NOT EXISTS ownership_transfer_requests_status_idx ON ownership_transfer_requests ("Status");

CREATE TABLE IF NOT EXISTS break_glass_tokens (
  "Id" uuid PRIMARY KEY,
  "TokenId" text NOT NULL UNIQUE,
  "TenantId" text NOT NULL,
  "UserId" text NOT NULL,
  "Role" text NOT NULL,
  "Scope" text NOT NULL,
  "Reason" text NOT NULL,
  "ExpiresUtc" timestamptz NOT NULL,
  "CreatedUtc" timestamptz NOT NULL,
  "RevokedUtc" timestamptz NULL
);

CREATE TABLE IF NOT EXISTS pack_activation_states (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "PackName" text NOT NULL,
  "PackHash" text NOT NULL,
  "Status" text NOT NULL,
  "ActivatedBy" text NOT NULL,
  "RolledBackBy" text NOT NULL,
  "RollbackReason" text NOT NULL,
  "ActivatedUtc" timestamptz NOT NULL,
  "RolledBackUtc" timestamptz NULL,
  "UpdatedUtc" timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS pack_activation_states_tenant_pack_idx ON pack_activation_states ("TenantId", "PackName");
CREATE INDEX IF NOT EXISTS pack_activation_states_status_idx ON pack_activation_states ("Status");

CREATE TABLE IF NOT EXISTS outcome_feedback (
  "Id" uuid PRIMARY KEY,
  "TenantId" text NOT NULL,
  "TraceId" text NOT NULL,
  "TicketId" text NULL,
  "ActionId" text NULL,
  "ActorId" text NOT NULL,
  "IdempotencyKey" text NOT NULL UNIQUE,
  "FeedbackType" text NOT NULL,
  "ConsentGranted" boolean NOT NULL DEFAULT false,
  "Notes" text NULL,
  "ConfidenceBefore" double precision NULL,
  "ConfidenceAfter" double precision NULL,
  "CreatedUtc" timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS outcome_feedback_tenant_id_idx ON outcome_feedback ("TenantId");
CREATE INDEX IF NOT EXISTS outcome_feedback_trace_id_idx ON outcome_feedback ("TraceId");
CREATE INDEX IF NOT EXISTS outcome_feedback_ticket_id_idx ON outcome_feedback ("TicketId");
CREATE INDEX IF NOT EXISTS outcome_feedback_action_id_idx ON outcome_feedback ("ActionId");
