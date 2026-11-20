using Cwi.Orchestrator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess;

public sealed class OrchestratorDbContext : DbContext
{
    public OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
        : base(options)
    {
    }

    public DbSet<TenantConfig> TenantConfigs => Set<TenantConfig>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ApprovalTicket> ApprovalTickets => Set<ApprovalTicket>();
    public DbSet<LedgerEvent> LedgerEvents => Set<LedgerEvent>();
    public DbSet<LedgerOutboxEntry> LedgerOutboxEntries => Set<LedgerOutboxEntry>();
    public DbSet<TelemetryWindow> TelemetryWindows => Set<TelemetryWindow>();
    public DbSet<ConnectorRun> ConnectorRuns => Set<ConnectorRun>();
    public DbSet<BreakGlassToken> BreakGlassTokens => Set<BreakGlassToken>();
    public DbSet<PackActivationState> PackActivationStates => Set<PackActivationState>();
    public DbSet<CanonicalRecord> CanonicalRecords => Set<CanonicalRecord>();
    public DbSet<OwnershipTransferRequest> OwnershipTransferRequests => Set<OwnershipTransferRequest>();
    public DbSet<OutcomeFeedbackRecord> OutcomeFeedbackRecords => Set<OutcomeFeedbackRecord>();
    public DbSet<OperatorProfile> OperatorProfiles => Set<OperatorProfile>();
    public DbSet<OwnershipLineageBinding> OwnershipLineageBindings => Set<OwnershipLineageBinding>();
    public DbSet<SuccessionPlan> SuccessionPlans => Set<SuccessionPlan>();
    public DbSet<SuccessionSnapshot> SuccessionSnapshots => Set<SuccessionSnapshot>();
    public DbSet<ArtifactRecord> ArtifactRecords => Set<ArtifactRecord>();
    public DbSet<AskHistoryRecord> AskHistoryRecords => Set<AskHistoryRecord>();
    public DbSet<JourneyEventRecord> JourneyEventRecords => Set<JourneyEventRecord>();
    public DbSet<HomeNodeRecord> HomeNodeRecords => Set<HomeNodeRecord>();
    public DbSet<HomeNodeAssignment> HomeNodeAssignments => Set<HomeNodeAssignment>();
    public DbSet<HomeNodePolicyScope> HomeNodePolicyScopes => Set<HomeNodePolicyScope>();
    public DbSet<GovernedMemoryRecord> GovernedMemoryRecords => Set<GovernedMemoryRecord>();
    public DbSet<ConversationSummaryRecord> ConversationSummaryRecords => Set<ConversationSummaryRecord>();
    public DbSet<SourceMemoryRecord> SourceMemoryRecords => Set<SourceMemoryRecord>();
    public DbSet<SourceChunkRecord> SourceChunkRecords => Set<SourceChunkRecord>();
    public DbSet<UserConnectorConnection> UserConnectorConnections => Set<UserConnectorConnection>();
    public DbSet<NormalizedConnectorRecordEntity> NormalizedConnectorRecords => Set<NormalizedConnectorRecordEntity>();
    public DbSet<ConnectorIngestionReceipt> ConnectorIngestionReceipts => Set<ConnectorIngestionReceipt>();
    public DbSet<MemoryFactIndexRecord> MemoryFactIndexRecords => Set<MemoryFactIndexRecord>();
    public DbSet<RecursiveEventRecord> RecursiveEvents => Set<RecursiveEventRecord>();
    public DbSet<RecursiveEventQuarantineRecord> RecursiveEventQuarantine => Set<RecursiveEventQuarantineRecord>();
    public DbSet<GraphUpdateCandidateRecord> GraphUpdateCandidates => Set<GraphUpdateCandidateRecord>();
    public DbSet<GraphVectorRefreshJobRecord> GraphVectorRefreshJobs => Set<GraphVectorRefreshJobRecord>();
    public DbSet<ValidatedCacheRecord> ValidatedCacheRecords => Set<ValidatedCacheRecord>();
    public DbSet<SustainabilityRequestRecord> SustainabilityRequestRecords => Set<SustainabilityRequestRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantConfig>(entity =>
        {
            entity.ToTable("tenant_config");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId).IsUnique();
            entity.Property(x => x.ConfigJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.ToTable("sessions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<ApprovalTicket>(entity =>
        {
            entity.ToTable("approval_tickets");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TicketId).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
        });

        modelBuilder.Entity<LedgerEvent>(entity =>
        {
            entity.ToTable("ledger_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TraceId);
        });

        modelBuilder.Entity<LedgerOutboxEntry>(entity =>
        {
            entity.ToTable("ledger_outbox");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedUtc);
            entity.HasIndex(x => x.PublishedUtc);
        });

        modelBuilder.Entity<TelemetryWindow>(entity =>
        {
            entity.ToTable("telemetry_windows");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ConnectorRun>(entity =>
        {
            entity.ToTable("connector_runs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
        });

        modelBuilder.Entity<BreakGlassToken>(entity =>
        {
            entity.ToTable("break_glass_tokens");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TokenId).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.UserId);
            entity.HasIndex(x => x.ExpiresUtc);
        });

        modelBuilder.Entity<PackActivationState>(entity =>
        {
            entity.ToTable("pack_activation_states");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.PackName }).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<CanonicalRecord>(entity =>
        {
            entity.ToTable("canonical_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RecordId).IsUnique();
            entity.Property(x => x.CanonicalJson).HasColumnType("jsonb");
            entity.Property(x => x.SourcesJson).HasColumnType("jsonb");
            entity.Property(x => x.ConflictsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<OwnershipTransferRequest>(entity =>
        {
            entity.ToTable("ownership_transfer_requests");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => x.ApprovalTicketId);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.ApproversJson).HasColumnType("jsonb");
            entity.Property(x => x.ApprovedByJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<OutcomeFeedbackRecord>(entity =>
        {
            entity.ToTable("outcome_feedback");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.TraceId);
            entity.HasIndex(x => x.TicketId);
            entity.HasIndex(x => x.ActionId);
        });

        modelBuilder.Entity<OperatorProfile>(entity =>
        {
            entity.ToTable("operator_profiles");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.OperatorId }).IsUnique();
        });

        modelBuilder.Entity<OwnershipLineageBinding>(entity =>
        {
            entity.ToTable("ownership_lineage_bindings");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.AssetId);
        });

        modelBuilder.Entity<SuccessionPlan>(entity =>
        {
            entity.ToTable("succession_plans");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PlanId).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.Property(x => x.SuccessorsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<SuccessionSnapshot>(entity =>
        {
            entity.ToTable("succession_snapshots");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SnapshotId).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.ToTable("artifact_registry");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.ArtifactId }).IsUnique();
            entity.HasIndex(x => x.TenantId);
            entity.Property(x => x.VersionsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<HomeNodeRecord>(entity =>
        {
            entity.ToTable("home_nodes");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.NodeId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.OwnerUserId });
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.DeviceClass).HasColumnName("device_class");
            entity.Property(x => x.TailscaleIp).HasColumnName("tailscale_ip");
            entity.Property(x => x.WatcherNodeId).HasColumnName("watcher_node_id");
            entity.Property(x => x.CapabilitiesJson).HasColumnType("jsonb");
            entity.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json");
            entity.Property(x => x.PackVersionsJson).HasColumnType("jsonb");
            entity.Property(x => x.PackVersionsJson).HasColumnName("pack_versions_json");
            entity.Property(x => x.PolicyProfileVersion).HasColumnName("policy_profile_version");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.LastHeartbeatUtc).HasColumnName("last_heartbeat_utc");
            entity.Property(x => x.AssignedUtc).HasColumnName("assigned_utc");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<HomeNodeAssignment>(entity =>
        {
            entity.ToTable("home_node_assignments");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.PrimaryHomeNodeId });
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.PrimaryHomeNodeId).HasColumnName("primary_home_node_id");
            entity.Property(x => x.WatcherNodeId).HasColumnName("watcher_node_id");
            entity.Property(x => x.PolicyScopeId).HasColumnName("policy_scope_id");
            entity.Property(x => x.LocalOnly).HasColumnName("local_only");
            entity.Property(x => x.RemoteFallbackAllowed).HasColumnName("remote_fallback_allowed");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.AssignedUtc).HasColumnName("assigned_utc");
            entity.Property(x => x.RevokedUtc).HasColumnName("revoked_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<HomeNodePolicyScope>(entity =>
        {
            entity.ToTable("home_node_policy_scopes");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.PolicyScopeId }).IsUnique();
            entity.Property(x => x.RulesJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.PolicyScopeId).HasColumnName("policy_scope_id");
            entity.Property(x => x.RulesJson).HasColumnName("rules_json");
            entity.Property(x => x.Version).HasColumnName("version");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<AskHistoryRecord>(entity =>
        {
            entity.ToTable("ask_history");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedUtc });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RequestId).HasColumnName("request_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.TurnId).HasColumnName("turn_id");
            entity.Property(x => x.Question).HasColumnName("question");
            entity.Property(x => x.Answer).HasColumnName("answer");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.ProvenanceId).HasColumnName("provenance_id");
            entity.Property(x => x.PackName).HasColumnName("pack_name");
            entity.Property(x => x.PackHash).HasColumnName("pack_hash");
            entity.Property(x => x.ContentSha256).HasColumnName("content_sha256");
            entity.Property(x => x.SummaryStatus).HasColumnName("summary_status");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.TraceJson).HasColumnName("trace_json");
            entity.Property(x => x.TraceJson).HasColumnType("jsonb");
            entity.Property(x => x.AttachmentsJson).HasColumnName("attachments_json");
            entity.Property(x => x.AttachmentsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<GovernedMemoryRecord>(entity =>
        {
            entity.ToTable("governed_memory");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.MemoryId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.MemoryScope, x.UpdatedUtc });
            entity.HasIndex(x => new { x.TenantId, x.NodeId, x.LocalOnly });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.MemoryId).HasColumnName("memory_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.MemoryScope).HasColumnName("memory_scope");
            entity.Property(x => x.MemoryType).HasColumnName("memory_type");
            entity.Property(x => x.MemoryText).HasColumnName("memory_text");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.SensitivityLevel).HasColumnName("sensitivity_level");
            entity.Property(x => x.RetentionPolicy).HasColumnName("retention_policy");
            entity.Property(x => x.AccessScope).HasColumnName("access_scope");
            entity.Property(x => x.LocalOnly).HasColumnName("local_only");
            entity.Property(x => x.SyncAllowed).HasColumnName("sync_allowed");
            entity.Property(x => x.ContentSha256).HasColumnName("content_sha256");
            entity.Property(x => x.ProvenanceId).HasColumnName("provenance_id");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<ConversationSummaryRecord>(entity =>
        {
            entity.ToTable("conversation_summaries");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.SummaryId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ConversationId, x.UpdatedUtc });
            entity.Property(x => x.CoveredTurnIdsJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SummaryId).HasColumnName("summary_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.SummaryText).HasColumnName("summary_text");
            entity.Property(x => x.CoveredTurnIdsJson).HasColumnName("covered_turn_ids_json");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.ContentSha256).HasColumnName("content_sha256");
            entity.Property(x => x.ProvenanceId).HasColumnName("provenance_id");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<SourceMemoryRecord>(entity =>
        {
            entity.ToTable("source_memory");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.SourceId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedUtc });
            entity.HasIndex(x => new { x.TenantId, x.NodeId, x.LocalOnly });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SourceId).HasColumnName("source_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.Filename).HasColumnName("filename");
            entity.Property(x => x.SourceType).HasColumnName("source_type");
            entity.Property(x => x.StorageLocation).HasColumnName("storage_location");
            entity.Property(x => x.ExtractionMethod).HasColumnName("extraction_method");
            entity.Property(x => x.LocalOnly).HasColumnName("local_only");
            entity.Property(x => x.SyncAllowed).HasColumnName("sync_allowed");
            entity.Property(x => x.ContentSha256).HasColumnName("content_sha256");
            entity.Property(x => x.ProvenanceId).HasColumnName("provenance_id");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<SourceChunkRecord>(entity =>
        {
            entity.ToTable("source_chunks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.SourceId, x.ChunkId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedUtc });
            entity.HasIndex(x => new { x.TenantId, x.NodeId, x.LocalOnly });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ChunkId).HasColumnName("chunk_id");
            entity.Property(x => x.SourceId).HasColumnName("source_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.ChunkText).HasColumnName("chunk_text");
            entity.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(x => x.HeadingContext).HasColumnName("heading_context");
            entity.Property(x => x.LocalOnly).HasColumnName("local_only");
            entity.Property(x => x.ContentSha256).HasColumnName("content_sha256");
            entity.Property(x => x.EmbeddingId).HasColumnName("embedding_id");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<UserConnectorConnection>(entity =>
        {
            entity.ToTable("user_connector_connections");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ConnectionId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ConnectorType });
            entity.HasIndex(x => x.Status);
            entity.Property(x => x.ScopesJson).HasColumnType("jsonb");
            entity.Property(x => x.ConfigJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ConnectionId).HasColumnName("connection_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConnectorType).HasColumnName("connector_type");
            entity.Property(x => x.DisplayName).HasColumnName("display_name");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.AuthType).HasColumnName("auth_type");
            entity.Property(x => x.CredentialRef).HasColumnName("credential_ref");
            entity.Property(x => x.ScopesJson).HasColumnName("scopes_json");
            entity.Property(x => x.ConfigJson).HasColumnName("config_json");
            entity.Property(x => x.SyncEnabled).HasColumnName("sync_enabled");
            entity.Property(x => x.SyncFrequencyMinutes).HasColumnName("sync_frequency_minutes");
            entity.Property(x => x.VisibilityScope).HasColumnName("visibility_scope");
            entity.Property(x => x.LocalOnly).HasColumnName("local_only");
            entity.Property(x => x.LastSyncStatus).HasColumnName("last_sync_status");
            entity.Property(x => x.LastSyncUtc).HasColumnName("last_sync_utc");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.ProvenanceId).HasColumnName("provenance_id");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            entity.Property(x => x.DisconnectedUtc).HasColumnName("disconnected_utc");
        });

        modelBuilder.Entity<NormalizedConnectorRecordEntity>(entity =>
        {
            entity.ToTable("normalized_connector_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.RecordId }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.ConnectorType, x.EventTime });
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.LifeWorkScope, x.SensitivityLabel });
            entity.Property(x => x.RecordJson).HasColumnType("jsonb");
            entity.Property(x => x.ProvenanceJson).HasColumnType("jsonb");
            entity.Property(x => x.SyncStateJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RecordId).HasColumnName("record_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.AccountId).HasColumnName("account_id");
            entity.Property(x => x.ConnectorType).HasColumnName("connector_type");
            entity.Property(x => x.SourceSystem).HasColumnName("source_system");
            entity.Property(x => x.SourceRecordId).HasColumnName("source_record_id");
            entity.Property(x => x.SourceThreadId).HasColumnName("source_thread_id");
            entity.Property(x => x.SourceUrl).HasColumnName("source_url");
            entity.Property(x => x.EventTime).HasColumnName("event_time");
            entity.Property(x => x.IngestedAt).HasColumnName("ingested_at");
            entity.Property(x => x.Title).HasColumnName("title");
            entity.Property(x => x.Body).HasColumnName("body");
            entity.Property(x => x.RawExcerpt).HasColumnName("raw_excerpt");
            entity.Property(x => x.LifeWorkScope).HasColumnName("life_work_scope");
            entity.Property(x => x.SensitivityLabel).HasColumnName("sensitivity_label");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.SourceSha256).HasColumnName("source_sha256");
            entity.Property(x => x.RecordJson).HasColumnName("record_json");
            entity.Property(x => x.ProvenanceJson).HasColumnName("provenance_json");
            entity.Property(x => x.SyncStateJson).HasColumnName("sync_state_json");
            entity.Property(x => x.IsDeleted).HasColumnName("is_deleted");
            entity.Property(x => x.IsUpdate).HasColumnName("is_update");
            entity.Property(x => x.Stale).HasColumnName("stale");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            entity.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
            entity.Property(x => x.ReplayCount).HasColumnName("replay_count");
        });

        modelBuilder.Entity<ConnectorIngestionReceipt>(entity =>
        {
            entity.ToTable("connector_ingestion_receipts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ReceiptId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.RecordId, x.CreatedUtc });
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.Status, x.CreatedUtc });
            entity.Property(x => x.MemoryIdsCreatedJson).HasColumnType("jsonb");
            entity.Property(x => x.MemoryIdsUpdatedJson).HasColumnType("jsonb");
            entity.Property(x => x.GraphEdgesCreatedJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ReceiptId).HasColumnName("receipt_id");
            entity.Property(x => x.RecordId).HasColumnName("record_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ConnectorType).HasColumnName("connector_type");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.MemoryIdsCreatedJson).HasColumnName("memory_ids_created_json");
            entity.Property(x => x.MemoryIdsUpdatedJson).HasColumnName("memory_ids_updated_json");
            entity.Property(x => x.GraphEdgesCreatedJson).HasColumnName("graph_edges_created_json");
            entity.Property(x => x.Error).HasColumnName("error");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<MemoryFactIndexRecord>(entity =>
        {
            entity.ToTable("memory_fact_index");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.FactKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.LifeWorkScope, x.MemoryDomain, x.MemoryType });
            entity.Property(x => x.RecordIdsJson).HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.FactKey).HasColumnName("fact_key");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.LifeWorkScope).HasColumnName("life_work_scope");
            entity.Property(x => x.MemoryDomain).HasColumnName("memory_domain");
            entity.Property(x => x.MemoryType).HasColumnName("memory_type");
            entity.Property(x => x.NormalizedFactText).HasColumnName("normalized_fact_text");
            entity.Property(x => x.RecordIdsJson).HasColumnName("record_ids_json");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            entity.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
        });

        modelBuilder.Entity<RecursiveEventRecord>(entity =>
        {
            entity.ToTable("recursive_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.EventType, x.TimestampUtc });
            entity.HasIndex(x => new { x.TenantId, x.TemporalState, x.TimestampUtc });
            entity.HasIndex(x => x.TraceId);
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.ProvenanceJson).HasColumnName("provenance_json").HasColumnType("jsonb");
            entity.Property(x => x.EventJson).HasColumnName("event_json").HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.SchemaVersion).HasColumnName("schema_version");
            entity.Property(x => x.EventType).HasColumnName("event_type");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.ScopeType).HasColumnName("scope_type");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.UserIdHash).HasColumnName("user_id_hash");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.SourceComponent).HasColumnName("source_component");
            entity.Property(x => x.SourceType).HasColumnName("source_type");
            entity.Property(x => x.SourceId).HasColumnName("source_id");
            entity.Property(x => x.SourceNodeId).HasColumnName("source_node_id");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.PolicyStatus).HasColumnName("policy_status");
            entity.Property(x => x.TemporalState).HasColumnName("temporal_state");
            entity.Property(x => x.TimestampUtc).HasColumnName("timestamp_utc");
            entity.Property(x => x.SourceHash).HasColumnName("source_hash");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<RecursiveEventQuarantineRecord>(entity =>
        {
            entity.ToTable("recursive_event_quarantine");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.QuarantineId).IsUnique();
            entity.HasIndex(x => x.EventId);
            entity.HasIndex(x => x.TenantId);
            entity.HasIndex(x => x.TraceId);
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.QuarantineId).HasColumnName("quarantine_id");
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.Reason).HasColumnName("reason");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.ValidationError).HasColumnName("validation_error");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<GraphUpdateCandidateRecord>(entity =>
        {
            entity.ToTable("graph_update_candidates");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CandidateId).IsUnique();
            entity.HasIndex(x => x.RecursiveEventId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.CandidateState, x.TemporalState });
            entity.HasIndex(x => new { x.TenantId, x.FactKey, x.TemporalState });
            entity.HasIndex(x => x.TraceId);
            entity.Property(x => x.ProvenanceJson).HasColumnName("provenance_json").HasColumnType("jsonb");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CandidateId).HasColumnName("candidate_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserIdHash).HasColumnName("user_id_hash");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.RecursiveEventId).HasColumnName("recursive_event_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.CandidateState).HasColumnName("candidate_state");
            entity.Property(x => x.TemporalState).HasColumnName("temporal_state");
            entity.Property(x => x.Subject).HasColumnName("subject");
            entity.Property(x => x.Relation).HasColumnName("relation");
            entity.Property(x => x.Object).HasColumnName("object");
            entity.Property(x => x.FactKey).HasColumnName("fact_key");
            entity.Property(x => x.Confidence).HasColumnName("confidence");
            entity.Property(x => x.SourceComponent).HasColumnName("source_component");
            entity.Property(x => x.SourceHash).HasColumnName("source_hash");
            entity.Property(x => x.ObservedUtc).HasColumnName("observed_utc");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<GraphVectorRefreshJobRecord>(entity =>
        {
            entity.ToTable("graph_vector_refresh_jobs");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.JobId).IsUnique();
            entity.HasIndex(x => x.GraphCandidateId).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.RefreshStatus, x.UpdatedUtc });
            entity.HasIndex(x => new { x.TenantId, x.FactKey, x.RefreshStatus });
            entity.HasIndex(x => x.TraceId);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.JobId).HasColumnName("job_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserIdHash).HasColumnName("user_id_hash");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.GraphCandidateId).HasColumnName("graph_candidate_id");
            entity.Property(x => x.RecursiveEventId).HasColumnName("recursive_event_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.FactKey).HasColumnName("fact_key");
            entity.Property(x => x.RefreshReason).HasColumnName("refresh_reason");
            entity.Property(x => x.RefreshStatus).HasColumnName("refresh_status");
            entity.Property(x => x.AffectedVectorScope).HasColumnName("affected_vector_scope");
            entity.Property(x => x.SourceHash).HasColumnName("source_hash");
            entity.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            entity.Property(x => x.LastError).HasColumnName("last_error");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
            entity.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        });

        modelBuilder.Entity<ValidatedCacheRecord>(entity =>
        {
            entity.ToTable("validated_cache_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.CacheKey }).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Scope, x.UserIdHash });
            entity.HasIndex(x => x.TraceId);
            entity.HasIndex(x => x.FreshUntilUtc);
            entity.Property(x => x.SourceProofJson).HasColumnName("source_proof_json").HasColumnType("jsonb");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.CacheKey).HasColumnName("cache_key");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.Scope).HasColumnName("scope");
            entity.Property(x => x.UserIdHash).HasColumnName("user_id_hash");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.FreshUntilUtc).HasColumnName("fresh_until_utc");
            entity.Property(x => x.PolicyMode).HasColumnName("policy_mode");
            entity.Property(x => x.PolicyVersion).HasColumnName("policy_version");
            entity.Property(x => x.PackHash).HasColumnName("pack_hash");
            entity.Property(x => x.ValidationStatus).HasColumnName("validation_status");
            entity.Property(x => x.CriticStatus).HasColumnName("critic_status");
            entity.Property(x => x.PayloadHash).HasColumnName("payload_hash");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<SustainabilityRequestRecord>(entity =>
        {
            entity.ToTable("sustainability_request_records");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => x.TraceId);
            entity.HasIndex(x => new { x.TenantId, x.TimestampUtc });
            entity.HasIndex(x => new { x.TenantId, x.UserIdHash, x.TimestampUtc });
            entity.HasIndex(x => new { x.TenantId, x.NodeId, x.TimestampUtc });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RequestId).HasColumnName("request_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.TimestampUtc).HasColumnName("timestamp_utc");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserIdHash).HasColumnName("user_id_hash");
            entity.Property(x => x.UserRole).HasColumnName("user_role");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.NodeName).HasColumnName("node_name");
            entity.Property(x => x.NodeGroup).HasColumnName("node_group");
            entity.Property(x => x.NodeRole).HasColumnName("node_role");
            entity.Property(x => x.NodeWatts).HasColumnName("node_watts");
            entity.Property(x => x.WattsSource).HasColumnName("watts_source");
            entity.Property(x => x.PrometheusInstance).HasColumnName("prometheus_instance");
            entity.Property(x => x.TelemetrySource).HasColumnName("telemetry_source");
            entity.Property(x => x.CalculationVersion).HasColumnName("calculation_version");
            entity.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(x => x.EnergyWh).HasColumnName("energy_wh");
            entity.Property(x => x.EnergyValueType).HasColumnName("energy_value_type");
            entity.Property(x => x.CostUsd).HasColumnName("cost_usd");
            entity.Property(x => x.CostValueType).HasColumnName("cost_value_type");
            entity.Property(x => x.WaterLiters).HasColumnName("water_liters");
            entity.Property(x => x.WaterValueType).HasColumnName("water_value_type");
            entity.Property(x => x.BaselineModelVersion).HasColumnName("baseline_model_version");
            entity.Property(x => x.CloudBaselineEnergyWh).HasColumnName("cloud_baseline_energy_wh");
            entity.Property(x => x.CloudBaselineEnergyValueType).HasColumnName("cloud_baseline_energy_value_type");
            entity.Property(x => x.CloudBaselineCostUsd).HasColumnName("cloud_baseline_cost_usd");
            entity.Property(x => x.CloudBaselineCostValueType).HasColumnName("cloud_baseline_cost_value_type");
            entity.Property(x => x.CloudBaselineWaterLiters).HasColumnName("cloud_baseline_water_liters");
            entity.Property(x => x.CloudBaselineWaterValueType).HasColumnName("cloud_baseline_water_value_type");
            entity.Property(x => x.EnergySavedWh).HasColumnName("energy_saved_wh");
            entity.Property(x => x.CostSavedUsd).HasColumnName("cost_saved_usd");
            entity.Property(x => x.WaterSavedLiters).HasColumnName("water_saved_liters");
            entity.Property(x => x.SavingsValueType).HasColumnName("savings_value_type");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.ModelOrRuntime).HasColumnName("model_or_runtime");
            entity.Property(x => x.Source).HasColumnName("source");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<JourneyEventRecord>(entity =>
        {
            entity.ToTable("journey_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.StreamId).IsUnique();
            entity.HasIndex(x => new { x.RequestId, x.CreatedUtc, x.Id });
            entity.HasIndex(x => new { x.TraceId, x.CreatedUtc, x.Id });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventId).HasColumnName("event_id");
            entity.Property(x => x.StreamId).HasColumnName("stream_id");
            entity.Property(x => x.RequestId).HasColumnName("request_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.Service).HasColumnName("service");
            entity.Property(x => x.Component).HasColumnName("component");
            entity.Property(x => x.Phase).HasColumnName("phase");
            entity.Property(x => x.Direction).HasColumnName("direction");
            entity.Property(x => x.FromComponent).HasColumnName("from_component");
            entity.Property(x => x.ToComponent).HasColumnName("to_component");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.PackName).HasColumnName("pack_name");
            entity.Property(x => x.Method).HasColumnName("method");
            entity.Property(x => x.Path).HasColumnName("path");
            entity.Property(x => x.StatusCode).HasColumnName("status_code");
            entity.Property(x => x.DurationMs).HasColumnName("duration_ms");
            entity.Property(x => x.Message).HasColumnName("message");
            entity.Property(x => x.MetadataJson).HasColumnName("metadata_json");
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });
    }
}
