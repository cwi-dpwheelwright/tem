using System.Text.Json.Serialization;

namespace Cwi.Router.Application.Routing;

public sealed class RouteAttestationEnvelope
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "v1";

    [JsonPropertyName("attestation_id")]
    public string AttestationId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("issued_utc")]
    public DateTimeOffset IssuedUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("decision_id")]
    public string? DecisionId { get; init; }

    [JsonPropertyName("selected_tier")]
    public string? SelectedTier { get; init; }

    [JsonPropertyName("selected_endpoint")]
    public string? SelectedEndpoint { get; init; }

    [JsonPropertyName("network_status")]
    public string? NetworkStatus { get; init; }

    [JsonPropertyName("network_score")]
    public double? NetworkScore { get; init; }

    [JsonPropertyName("mesh_health_status")]
    public string? MeshHealthStatus { get; init; }

    [JsonPropertyName("preferred_interface")]
    public string? PreferredInterface { get; init; }

    [JsonPropertyName("reroute_required")]
    public bool RerouteRequired { get; init; }

    [JsonPropertyName("reroute_reason")]
    public string? RerouteReason { get; init; }

    [JsonPropertyName("nonce")]
    public string Nonce { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("reason_codes")]
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }
}
