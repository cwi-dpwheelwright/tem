namespace Cwi.Coordination.Application;

public sealed class ApplicationOptions
{
    public string ProtocolVersion { get; init; } = "1.0";
    public bool RequireClientCertificate { get; init; } = true;
}
