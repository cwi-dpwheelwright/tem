#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class OwnershipLineageNodeDto
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

public sealed class OwnershipLineageEdgeDto
{
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string Relationship { get; init; } = string.Empty;
}

public sealed class OwnershipLineageResponseDto
{
    [JsonPropertyName("nodes")]
    public IReadOnlyList<OwnershipLineageNodeDto> Nodes { get; init; } =
        Array.Empty<OwnershipLineageNodeDto>();

    [JsonPropertyName("edges")]
    public IReadOnlyList<OwnershipLineageEdgeDto> Edges { get; init; } =
        Array.Empty<OwnershipLineageEdgeDto>();
}

public sealed class OwnershipRevocationEntryDto
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("requested_by")]
    public string RequestedBy { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}
#nullable disable
