using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Survival;

public sealed class SurvivalSignalDto
{
    [JsonPropertyName("signal_type")]
    public string SignalType { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public double Severity { get; init; }

    [JsonPropertyName("details")]
    public string? Details { get; init; }

    [JsonPropertyName("observed_utc")]
    public DateTimeOffset ObservedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class SurvivalStatusDto
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "normal";

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = string.Empty;

    [JsonPropertyName("last_signal_utc")]
    public DateTimeOffset? LastSignalUtc { get; init; }

    [JsonPropertyName("offline_message")]
    public string OfflineMessage { get; init; } = string.Empty;
}

public sealed class OfflineResponseDto
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "normal";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
