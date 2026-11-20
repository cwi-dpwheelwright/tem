namespace Cwi.Coordination.Composition;

public sealed class CoordinationCompositionOptions
{
    public string ProtocolVersion { get; init; } = "1.0";
    public bool RequireClientCertificate { get; init; } = true;
}
