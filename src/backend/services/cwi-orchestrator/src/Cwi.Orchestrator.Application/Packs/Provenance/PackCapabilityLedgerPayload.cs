using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Packs.Provenance;

public sealed class PackCapabilityLedgerPayload
{
    public string TraceId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackHash { get; init; } = string.Empty;
    public string PackVersion { get; init; } = string.Empty;
    public string PolicyMode { get; init; } = string.Empty;
    public string? ModelId { get; init; }
    public string? PolicyVersion { get; init; }
    public IReadOnlyList<PackCapabilitySummary> Capabilities { get; init; } = Array.Empty<PackCapabilitySummary>();
    public IReadOnlyList<string> BlockingCapabilities { get; init; } = Array.Empty<string>();
    public string DecisionReason { get; init; } = string.Empty;
    public bool RequiresApproval { get; init; }
    public bool AllowExecution { get; init; }
    public string Risk { get; init; } = string.Empty;
    public DateTimeOffset EvaluatedUtc { get; init; }
}
