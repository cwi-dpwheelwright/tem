#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Ingest;

public sealed class PackIngestRequest
{
    public string BundlePath { get; init; } = string.Empty;
    public string BundleName { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public string? PackIdOverride { get; init; }
    public string? PackVersionOverride { get; init; }
    public string? Notes { get; init; }
    public string? TenantId { get; init; }
}

public sealed class PackIngestResponseDto
{
    public string PackId { get; init; } = string.Empty;
    public string PackVersion { get; init; } = string.Empty;
    public string Anchor { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public string? Notes { get; init; }
    public DateTimeOffset UploadedUtc { get; init; }
    public string? Message { get; init; }
}
#nullable disable
