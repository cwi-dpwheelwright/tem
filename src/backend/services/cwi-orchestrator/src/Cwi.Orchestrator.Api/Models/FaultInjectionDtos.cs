#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Api.Models;

public sealed class FaultInjectionRequest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

public sealed class FaultInjectionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "ok";

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}
#nullable disable
