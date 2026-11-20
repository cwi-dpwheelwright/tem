namespace Cwi.Router.Application.Health;

public sealed class MeshHealthSnapshot
{
    public string Status { get; init; } = "unknown";
    public int TotalNodes { get; init; }
    public int DegradedNodes { get; init; }
    public int DownNodes { get; init; }
    public int TotalLinks { get; init; }
    public int DegradedLinks { get; init; }
    public int DownLinks { get; init; }
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
