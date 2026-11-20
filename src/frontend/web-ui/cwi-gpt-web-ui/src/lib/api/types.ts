export type ModelTier = "edge" | "free" | "pro" | "unknown";

export interface Trace {
  request_id?: string;
  trace_id?: string;
  sources?: string[];
  confidence?: number;
  confidence_level?: string;
  confidence_basis?: string;
  grounding_mode?: string;
  grounded?: boolean;
  retrieval_allowed?: boolean;
  retrieval_skipped_reason?: string;
  retrieval_status?: string;
  retrieval_domain?: string;
  retrieval_domain_terms?: string[];
  reasoning_based?: boolean;
  route?: string;
  memory_used?: boolean;
  memory_framework_version?: string;
  memory_active_context_count?: number;
  memory_excluded_context_count?: number;
  memory_framework_decisions?: Array<{
    memory_id: string;
    slot: string;
    outcome: string;
    reason: string;
    temporal_state: string;
  }>;
  memory_conflicts?: Array<{
    conflict_id: string;
    slot: string;
    memory_ids: string[];
  }>;
  graph_used?: boolean;
  pack_used?: boolean;
  graph_hits?: number;
  document_hits?: number;
  policy_mode?: string;
  model?: string;
  pack_hash?: string;
  pack_version?: string;
  intent_type?: string;
  intent_mode?: string;
  audience_mode?: string;
  verbosity_mode?: string;
  tone_mode?: string;
  comprehension_expression_version?: string;
  context_awareness_mode?: string;
  clarity_decision?: string;
  clarity_reason?: string;
  expression_tone_profile?: string;
  expression_tone_reason?: string;
  template_used?: string;
  presentation_framework_version?: string;
  presentation_template_family?: string;
  presentation_template_reason?: string;
  presentation_rhythm_mode?: string;
  presentation_rhythm_reason?: string;
  presentation_formatting_mode?: string;
  presentation_formatting_reason?: string;
  presentation_source_display_mode?: string;
  presentation_source_display_reason?: string;
  communication_runtime_applied?: boolean;
  evidence_required?: boolean;
  warnings?: string[];
  memory_updated?: boolean;
  applied_preference_mode?: string;
  reasoning_receipt?: {
    graph_used?: boolean;
    grounded?: boolean;
    grounding_mode?: string;
    retrieval_allowed?: boolean;
    retrieval_skipped_reason?: string;
    confidence_basis?: string;
    graph_query_template?: string;
    graph_terms?: string[];
    graph_fallback_used?: boolean;
    graph_failure_reason?: string;
    graph_evidence_count?: number;
    document_evidence_count?: number;
  };
  otel_trace_id?: string;
  execution_state?: string;
  execution_target?: string;
  deferred_work_id?: string;
  home_node_status?: string;
  execution_message?: string;
  trusted_web_requested?: boolean;
  trusted_web_enabled?: boolean;
  trusted_web_profile?: string;
  trusted_web_hits?: number;
  trusted_web_skipped_reason?: string;
  trusted_web_retrieval_duration_ms?: number;
  trusted_web_sources?: Array<{
    source_id: string;
    evidence_source_id?: string;
    marker?: number;
    source_type?: string;
    trust_tier?: string;
    publisher?: string;
    title: string;
    url: string;
    canonical_url?: string;
    chunk_id: string;
    excerpt?: string;
    content_sha256?: string;
    score: number;
    retrieved_utc: string;
  }>;
}

export interface AskCitation {
  doc_id: string;
  chunk_id: string;
  score?: number | null;
  marker?: number | null;
  source_id?: string | null;
  source_type?: string | null;
  title?: string | null;
  url?: string | null;
  excerpt?: string | null;
  content_sha256?: string | null;
  retrieved_utc?: string | null;
}

export interface AskRequest {
  tenant_id?: string;
  user_id?: string;
  question: string;
  pack?: {
    name?: string;
    anchor?: string;
  };
  context?: {
    mode?: string;
    top_k?: number;
    use_web?: boolean;
    trusted_web_requested?: boolean;
    conversation_id?: string;
    timezone?: string;
    web_retrieval?: {
      enabled: boolean;
      mode?: "include";
      profile?: string;
      max_results?: number;
    };
  };
}

export interface AskResponse {
  answer: string;
  answer_markdown?: string;
  execution_state?: string;
  execution_target?: string;
  deferred_work_id?: string | null;
  home_node_status?: string | null;
  execution_message?: string | null;
  citations: AskCitation[];
  intent_mode?: string | null;
  audience_mode?: string | null;
  template_used?: string | null;
  verbosity_mode?: string | null;
  confidence?: number | null;
  confidence_level?: string | null;
  packs_used?: string[] | null;
  sources?: string[] | null;
  provenance?: {
    request_id?: string | null;
    trace_id?: string | null;
    grounding_mode?: string | null;
    model?: string | null;
    pack_hash?: string | null;
    pack_version?: string | null;
  } | null;
  warnings?: string[] | null;
  unsupported_claims?: string[] | null;
  trace?: Trace | null;
}

export interface IngestDocumentRequest {
  version: string;
  tenant_id: string;
  user_id?: string;
  idempotency_key?: string;
  tags?: string[];
  document: {
    doc_id: string;
    title: string;
    mime_type: string;
    content: string;
    content_encoding: "plain" | "base64";
    source: string;
    created_utc?: string;
    metadata?: Record<string, unknown>;
  };
}

export interface IngestDocumentResponse {
  accepted: boolean;
  ingest_id: string;
  trace_id?: string | null;
  doc_id?: string | null;
  message?: string | null;
}

export interface HomeNodeDto {
  tenant_id: string;
  node_id: string;
  owner_user_id: string;
  device_class?: string | null;
  tailscale_ip?: string | null;
  watcher_node_id?: string | null;
  capabilities?: Record<string, unknown>;
  pack_versions?: Record<string, unknown>;
  policy_profile_version?: string | null;
  status: string;
  last_heartbeat_utc?: string | null;
  assigned_utc?: string | null;
  created_utc: string;
  updated_utc: string;
}

export interface HomeNodeAssignmentDto {
  tenant_id: string;
  user_id: string;
  primary_home_node_id: string;
  watcher_node_id?: string | null;
  policy_scope_id?: string | null;
  local_only: boolean;
  remote_fallback_allowed: boolean;
  status: string;
  assigned_utc: string;
  revoked_utc?: string | null;
  updated_utc: string;
}

export interface AccountHomeNodeLinkRequest {
  node_id?: string;
  tailscale_ip?: string;
  qr_code_payload?: string;
  watcher_node_id?: string;
  policy_scope_id?: string;
  local_only?: boolean;
  remote_fallback_allowed?: boolean;
}

export interface AccountHomeNodeLinkResponse {
  tenant_id: string;
  user_id: string;
  linked: boolean;
  matched_identifier?: string | null;
  matched_via?: string | null;
  assignment?: HomeNodeAssignmentDto | null;
  node?: HomeNodeDto | null;
}

export interface ConnectorCatalogItem {
  connector_type: string;
  display_name: string;
  description: string;
  auth_types: string[];
  default_scopes: string[];
  configurable_fields: string[];
  supports_sync: boolean;
  authorization_configured: boolean;
}

export interface AccountConnectorConnection {
  connection_id: string;
  tenant_id: string;
  user_id: string;
  connector_type: string;
  display_name: string;
  status: string;
  auth_type: string;
  credential_configured: boolean;
  authorization_url?: string | null;
  authorization_configured: boolean;
  credential_ref?: string | null;
  scopes: string[];
  config: Record<string, unknown>;
  sync_enabled: boolean;
  sync_frequency_minutes: number;
  visibility_scope: "private" | "tenant" | "home_node" | string;
  local_only: boolean;
  last_sync_status?: string | null;
  last_sync_utc?: string | null;
  last_error?: string | null;
  provenance_id?: string | null;
  created_utc: string;
  updated_utc: string;
  disconnected_utc?: string | null;
}

export interface AccountConnectorListResponse {
  available: ConnectorCatalogItem[];
  connections: AccountConnectorConnection[];
}

export interface AccountConnectorCreateRequest {
  connector_type: string;
  display_name?: string;
  auth_type?: string;
  credential_reference?: string;
  scopes?: string[];
  config?: Record<string, unknown>;
  sync_enabled?: boolean;
  sync_frequency_minutes?: number;
  visibility_scope?: "private" | "tenant" | "home_node";
  local_only?: boolean;
}

export interface AccountConnectorUpdateRequest {
  display_name?: string;
  status?: "connected" | "pending_auth" | "paused";
  credential_reference?: string;
  scopes?: string[];
  config?: Record<string, unknown>;
  sync_enabled?: boolean;
  sync_frequency_minutes?: number;
  visibility_scope?: "private" | "tenant" | "home_node";
  local_only?: boolean;
}

export interface ActionRequest {
  tenant_id?: string;
  user_id?: string;
  policy_mode?: string;
  action: {
    type: string;
    payload?: Record<string, unknown>;
  };
  pack_hash?: string;
  pack_version?: string;
}

export interface ApprovalTicket {
  ticket_id: string;
  risk?: string;
  reason?: string;
}

export interface ActionResponse {
  executed: boolean;
  approval_ticket?: ApprovalTicket;
  result?: unknown;
  trace?: Trace | null;
}

export interface ApprovalTicketStatus {
  ticket_id: string;
  status: string;
  risk?: string;
  reason?: string;
  updated_utc?: string;
}

export interface TraceLookupResponse {
  trace_id: string;
  doc_id?: string | null;
}

export interface AskHistoryRecordRequest {
  request_id?: string;
  conversation_id?: string;
  question: string;
  answer: string;
  response_status?: "processing" | "completed" | "failed" | string;
  trace_id?: string;
  pack_name?: string;
  pack_hash?: string;
  trace?: Trace | null;
  attachments?: AskHistoryAttachment[];
  max_entries?: number;
  retention_days?: number;
}

export interface AskHistoryAttachment {
  name: string;
  mime_type: string;
  size: number;
  kind: "file" | "image";
  excerpt?: string;
  truncated?: boolean;
  preview_data_url?: string;
}

export interface AskHistoryRecord {
  id: string;
  request_id?: string;
  conversation_id?: string;
  turn_id?: string;
  question: string;
  answer: string;
  response_status?: "processing" | "completed" | "failed" | string;
  created_at: string;
  trace_id?: string;
  provenance_id?: string;
  pack_name?: string;
  pack_hash?: string;
  content_sha256?: string;
  summary_status?: string;
  trace?: Trace | null;
  attachments?: AskHistoryAttachment[];
}

export interface AskHistoryListResponse {
  items: AskHistoryRecord[];
}

export interface AskHistoryClearResponse {
  deleted: number;
}

export interface JourneyEvent {
  event_id: string;
  request_id: string;
  trace_id?: string;
  service: string;
  component: string;
  phase: string;
  direction?: string;
  from_component?: string;
  to_component?: string;
  tenant_id?: string;
  user_id?: string;
  pack_name?: string;
  method?: string;
  path?: string;
  status_code?: number;
  duration_ms?: number;
  message?: string;
  created_utc: string;
  metadata?: Record<string, unknown>;
}

export interface JourneyListResponse {
  request_id: string;
  items: JourneyEvent[];
}

export interface FeedbackReportRequest {
  tenant_id: string;
  tenant_name?: string | null;
  user_id: string;
  user_email?: string | null;
  user_role?: string | null;
  session_id?: string | null;
  conversation_id?: string | null;
  request_id?: string | null;
  trace_id?: string | null;
  selected_pack?: {
    pack_id?: string | null;
    pack_version?: string | null;
    domain?: string | null;
  };
  question: string;
  answer: string;
  confidence?: number | null;
  runtime_trace?: {
    route?: string | null;
    intent?: string | null;
    model_used?: string | null;
    memory_used?: boolean | null;
    graph_used?: boolean | null;
    retrieval_used?: boolean | null;
    pack_used?: boolean | null;
    connector_context_used?: boolean | null;
    latency_ms?: number | null;
  };
  feedback: {
    type: string;
    severity: string;
    comment?: string | null;
  };
  screenshot?: {
    included: boolean;
    data_url?: string | null;
  };
  client?: {
    ui_page?: string | null;
    frontend_app_version?: string | null;
    backend_runtime_version?: string | null;
    browser_user_agent?: string | null;
  };
}

export interface FeedbackReportResponse {
  ok: boolean;
  feedback_id: string;
  smartsheet_row_id?: string | null;
  screenshot_stored: boolean;
  message: string;
  warnings?: string[] | null;
}
