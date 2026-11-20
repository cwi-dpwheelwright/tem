using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackIngestResponse
{
    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("pack_version")]
    public string PackVersion { get; set; } = string.Empty;

    [JsonPropertyName("anchor")]
    public string Anchor { get; set; } = string.Empty;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("uploaded_utc")]
    public DateTimeOffset UploadedUtc { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
