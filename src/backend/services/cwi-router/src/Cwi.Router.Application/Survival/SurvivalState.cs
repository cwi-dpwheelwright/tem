namespace Cwi.Router.Application.Survival;

public sealed class SurvivalState
{
    public string Mode { get; init; } = "normal";
    public string? Profile { get; init; }
    public DateTimeOffset? LastSignalUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "unknown";
}
