#nullable enable
namespace Cwi.Orchestrator.Application.Inference;

public sealed class FallbackSelection
{
    public bool Applied { get; init; }
    public string? Answer { get; init; }
    public string? Reason { get; init; }
    public string? Mode { get; init; }
}
#nullable disable
