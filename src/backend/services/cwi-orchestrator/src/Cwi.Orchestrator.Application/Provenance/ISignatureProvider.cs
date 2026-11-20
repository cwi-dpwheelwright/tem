#nullable enable
namespace Cwi.Orchestrator.Application.Provenance;

public sealed record SignatureResult(string Signature, string Algorithm, string KeyId);

public interface ISignatureProvider
{
    SignatureResult? Sign(string data);
    bool Verify(string data, SignatureResult signature);
}
#nullable disable
