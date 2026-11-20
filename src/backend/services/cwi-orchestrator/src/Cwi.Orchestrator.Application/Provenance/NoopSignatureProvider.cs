#nullable enable
namespace Cwi.Orchestrator.Application.Provenance;

public sealed class NoopSignatureProvider : ISignatureProvider
{
    public SignatureResult? Sign(string data)
    {
        return null;
    }

    public bool Verify(string data, SignatureResult signature)
    {
        return true;
    }
}
#nullable disable
