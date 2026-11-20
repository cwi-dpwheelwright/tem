using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackDiagnosticsIssue
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("dependency_pack_id")]
    public string? DependencyPackId { get; set; }
}
