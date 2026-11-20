namespace Cwi.Coordination.Application.Services;

public sealed class RoundOrchestrator
{
    private readonly LeaderElectionService _election;

    public RoundOrchestrator(LeaderElectionService election)
    {
        _election = election;
    }

    public bool TryBecomeLeader(string nodeId)
    {
        return _election.TryAcquire(nodeId, TimeSpan.FromSeconds(30));
    }

    public bool RenewLeadership(string nodeId)
    {
        return _election.Renew(nodeId, TimeSpan.FromSeconds(30));
    }
}
