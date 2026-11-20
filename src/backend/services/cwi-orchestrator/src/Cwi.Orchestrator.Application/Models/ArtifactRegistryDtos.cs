#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ArtifactRegistrationRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("artifact_type")]
    public string ArtifactType { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("provenance_refs")]
    public IReadOnlyList<string> ProvenanceRefs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class ArtifactRegistrationResponseDto
{
    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class ArtifactSummaryDto
{
    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("artifact_type")]
    public string ArtifactType { get; init; } = string.Empty;

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

public sealed class ArtifactDetailDto
{
    [JsonPropertyName("artifact_id")]
    public string ArtifactId { get; init; } = string.Empty;

    [JsonPropertyName("artifact_type")]
    public string ArtifactType { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("versions")]
    public IReadOnlyList<ArtifactVersionDto> Versions { get; init; } = Array.Empty<ArtifactVersionDto>();
}

public sealed class ArtifactVersionDto
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    [JsonPropertyName("provenance_refs")]
    public IReadOnlyList<string> ProvenanceRefs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}
#nullable disable
