using System.Threading;

namespace Cwi.Router.Application.Metrics;

public sealed class RouterMetrics
{
    private long _survivalPriorityAllowed;
    private long _survivalPriorityBlocked;

    public void RecordSurvivalPriorityAllowed()
    {
        Interlocked.Increment(ref _survivalPriorityAllowed);
    }

    public void RecordSurvivalPriorityBlocked()
    {
        Interlocked.Increment(ref _survivalPriorityBlocked);
    }

    public (long Allowed, long Blocked) SnapshotSurvivalPriority()
    {
        return (Interlocked.Read(ref _survivalPriorityAllowed), Interlocked.Read(ref _survivalPriorityBlocked));
    }
}
