namespace Cwi.Router.Application.Routing;

public sealed class RouteAttestationValidator
{
    private readonly RouteAttestationReplayStore _store;

    public RouteAttestationValidator(RouteAttestationReplayStore store)
    {
        _store = store;
    }

    public bool TryValidate(RouteAttestationEnvelope envelope, out string? reason)
    {
        if (envelope is null)
        {
            reason = "attestation_missing";
            return false;
        }

        var key = ResolveKey(envelope);
        return _store.TryRegister(key, envelope.Nonce, envelope.Sequence, out reason);
    }

    private static string ResolveKey(RouteAttestationEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.NodeId))
        {
            return envelope.NodeId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.TenantId))
        {
            return envelope.TenantId;
        }

        if (!string.IsNullOrWhiteSpace(envelope.TraceId))
        {
            return envelope.TraceId;
        }

        return "unknown";
    }
}
