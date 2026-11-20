using System.Text.Json;
using Cwi.Coordination.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cwi.Coordination.DataAccess;

public sealed class CoordinationDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CoordinationDbContext(DbContextOptions<CoordinationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<LedgerEvent> LedgerEvents => Set<LedgerEvent>();
    public DbSet<LinkState> LinkStates => Set<LinkState>();
    public DbSet<MeshEvent> MeshEvents => Set<MeshEvent>();
    public DbSet<DegradationEvent> DegradationEvents => Set<DegradationEvent>();
    public DbSet<PlacementMetric> PlacementMetrics => Set<PlacementMetric>();
    public DbSet<BackhaulPolicyRecord> BackhaulPolicies => Set<BackhaulPolicyRecord>();
    public DbSet<SurvivalModeRecord> SurvivalModes => Set<SurvivalModeRecord>();
    public DbSet<NodeWorkItem> NodeWorkItems => Set<NodeWorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>(entity =>
        {
            entity.ToTable("coordination_members");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NodeId).IsUnique();
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(x => x.DeviceClass).HasColumnName("device_class");
            entity.Property(x => x.TailscaleIp).HasColumnName("tailscale_ip");
            entity.Property(x => x.WatcherNodeId).HasColumnName("watcher_node_id");
            entity.Property(x => x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("jsonb");
            entity.Property(x => x.PackVersionsJson).HasColumnName("pack_versions_json").HasColumnType("jsonb");
            entity.Property(x => x.PolicyProfileVersion).HasColumnName("policy_profile_version");
            entity.Property(x => x.HealthJson).HasColumnName("health_json").HasColumnType("jsonb");
            entity.Property(x => x.CertificateFingerprint).HasColumnName("certificate_fingerprint");
            entity.Property(x => x.Status).HasConversion<string>();
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.JoinedUtc).HasColumnName("joined_utc");
            entity.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
            entity.Property(x => x.RevokedUtc).HasColumnName("revoked_utc");
        });

        modelBuilder.Entity<Round>(entity =>
        {
            entity.ToTable("coordination_rounds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>();
        });

        modelBuilder.Entity<LedgerEvent>(entity =>
        {
            entity.ToTable("coordination_ledger_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedUtc);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EventType).HasColumnName("event_type");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json");
            entity.Property(x => x.PrevHash).HasColumnName("prev_hash");
            entity.Property(x => x.Hash).HasColumnName("hash");
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        });

        modelBuilder.Entity<LinkState>(entity =>
        {
            entity.ToTable("coordination_link_states");
            entity.HasKey(x => new { x.NodeId, x.Interface });
            entity.Property(x => x.Interface).HasConversion<string>().HasColumnName("interface_kind");
            entity.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
        });

        modelBuilder.Entity<MeshEvent>(entity =>
        {
            entity.ToTable("coordination_mesh_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedUtc);
            entity.Property(x => x.Interface).HasConversion<string>().HasColumnName("interface_kind");
            entity.Property(x => x.EventType).HasConversion<string>().HasColumnName("event_type");
        });

        var dictConverter = new ValueConverter<Dictionary<string, object?>, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                ?? new Dictionary<string, object?>());

        var nullableDictConverter = new ValueConverter<Dictionary<string, object?>?, string?>(
            value => value == null ? null : JsonSerializer.Serialize(value, JsonOptions),
            json => string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions));

        var radioConverter = new ValueConverter<PlacementRadioMetrics, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<PlacementRadioMetrics>(json, JsonOptions)
                ?? new PlacementRadioMetrics());

        var environmentConverter = new ValueConverter<PlacementEnvironmentMetrics, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<PlacementEnvironmentMetrics>(json, JsonOptions)
                ?? new PlacementEnvironmentMetrics());

        var uncertaintyConverter = new ValueConverter<PlacementUncertaintyMetrics, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<PlacementUncertaintyMetrics>(json, JsonOptions)
                ?? new PlacementUncertaintyMetrics());

        modelBuilder.Entity<DegradationEvent>(entity =>
        {
            entity.ToTable("coordination_degradation_events");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EventId).IsUnique();
            entity.HasIndex(x => x.ReceivedUtc);
            entity.Property(x => x.Details).HasConversion(dictConverter).HasColumnType("jsonb");
            entity.Property(x => x.Provenance).HasConversion(nullableDictConverter).HasColumnType("jsonb");
        });

        modelBuilder.Entity<PlacementMetric>(entity =>
        {
            entity.ToTable("coordination_placement_metrics");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.MetricsId).IsUnique();
            entity.HasIndex(x => x.NodeId);
            entity.HasIndex(x => x.ObservedUtc);
            entity.Property(x => x.MetricsId).HasColumnName("metrics_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.ObservedUtc).HasColumnName("observed_utc");
            entity.Property(x => x.ScenarioId).HasColumnName("scenario_id");
            entity.Property(x => x.Radio).HasConversion(radioConverter).HasColumnType("jsonb").HasColumnName("radio");
            entity.Property(x => x.Environment).HasConversion(environmentConverter).HasColumnType("jsonb").HasColumnName("environment");
            entity.Property(x => x.Uncertainty).HasConversion(uncertaintyConverter).HasColumnType("jsonb").HasColumnName("uncertainty");
            entity.Property(x => x.Metadata).HasConversion(nullableDictConverter).HasColumnType("jsonb").HasColumnName("metadata");
        });

        modelBuilder.Entity<BackhaulPolicyRecord>(entity =>
        {
            entity.ToTable("coordination_backhaul_policy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RequireAuthenticatedChannel).HasColumnName("require_authenticated_channel");
            entity.Property(x => x.AllowedInterfacesJson).HasColumnType("jsonb").HasColumnName("allowed_interfaces");
            entity.Property(x => x.DeniedInterfacesJson).HasColumnType("jsonb").HasColumnName("denied_interfaces");
            entity.Property(x => x.RevokedPeersJson).HasColumnType("jsonb").HasColumnName("revoked_peers");
            entity.Property(x => x.PolicyVersion).HasColumnName("policy_version");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });

        modelBuilder.Entity<SurvivalModeRecord>(entity =>
        {
            entity.ToTable("coordination_survival_mode");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<NodeWorkItem>(entity =>
        {
            entity.ToTable("coordination_node_work_items");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.WorkId).IsUnique();
            entity.HasIndex(x => new { x.NodeId, x.Status, x.AvailableUtc });
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.WorkId).HasColumnName("work_id");
            entity.Property(x => x.RequestId).HasColumnName("request_id");
            entity.Property(x => x.TraceId).HasColumnName("trace_id");
            entity.Property(x => x.NodeId).HasColumnName("node_id");
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.TaskType).HasColumnName("task_type");
            entity.Property(x => x.PolicyScope).HasColumnName("policy_scope");
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
            entity.Property(x => x.WorkToken).HasColumnName("work_token");
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
            entity.Property(x => x.CreatedUtc).HasColumnName("created_utc");
            entity.Property(x => x.AvailableUtc).HasColumnName("available_utc");
            entity.Property(x => x.ExpiresUtc).HasColumnName("expires_utc");
            entity.Property(x => x.LeaseId).HasColumnName("lease_id");
            entity.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
            entity.Property(x => x.LeaseCount).HasColumnName("lease_count");
            entity.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
            entity.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
            entity.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        });
    }
}
