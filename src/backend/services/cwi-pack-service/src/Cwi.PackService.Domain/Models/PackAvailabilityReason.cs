using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackAvailabilityReason
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
