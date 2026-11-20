using System.Text.Json.Serialization;

namespace Cwi.Router.Api.Models;

public sealed class ErrorResponse
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("error")]
    public string Error { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }
}
