namespace Cwi.Coordination.Api.Security;

public sealed class TransportOptions
{
    public bool RequireClientCertificate { get; init; } = true;
    public string? TrustBundlePath { get; init; }
}
