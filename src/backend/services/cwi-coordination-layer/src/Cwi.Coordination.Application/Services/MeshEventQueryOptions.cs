namespace Cwi.Coordination.Application.Services;

public sealed class MeshEventQueryOptions
{
    public int DefaultLimit { get; init; } = 50;
    public int MaxLimit { get; init; } = 200;
    public int RetentionDays { get; init; } = 30;

    public int NormalizeLimit(int? requested)
    {
        if (!requested.HasValue || requested.Value <= 0)
        {
            return DefaultLimit;
        }

        var max = MaxLimit <= 0 ? DefaultLimit : MaxLimit;
        return Math.Min(requested.Value, max);
    }
}
