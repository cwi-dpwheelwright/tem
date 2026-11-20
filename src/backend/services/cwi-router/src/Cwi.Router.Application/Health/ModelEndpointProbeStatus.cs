namespace Cwi.Router.Application.Health;

public sealed class ModelEndpointProbeStatus
{
    public DateTimeOffset? LastCheckedUtc { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public string? Detail { get; init; }

    public ModelEndpointProbeStatus WithSuccess(DateTimeOffset now)
    {
        return new ModelEndpointProbeStatus
        {
            LastCheckedUtc = now,
            LastSuccessUtc = now,
            Detail = null
        };
    }

    public ModelEndpointProbeStatus WithFailure(DateTimeOffset now, string detail)
    {
        return new ModelEndpointProbeStatus
        {
            LastCheckedUtc = now,
            LastSuccessUtc = LastSuccessUtc,
            Detail = detail
        };
    }
}
