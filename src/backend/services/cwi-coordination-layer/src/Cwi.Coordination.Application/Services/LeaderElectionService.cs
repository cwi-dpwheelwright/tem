namespace Cwi.Coordination.Application.Services;

public sealed class LeaderElectionService
{
    private readonly object _lock = new();
    private string? _leaderNodeId;
    private DateTimeOffset? _leaseExpiresUtc;

    public bool TryAcquire(string nodeId, TimeSpan leaseDuration)
    {
        lock (_lock)
        {
            if (_leaderNodeId is null || LeaseExpired())
            {
                _leaderNodeId = nodeId;
                _leaseExpiresUtc = DateTimeOffset.UtcNow.Add(leaseDuration);
                return true;
            }
            return string.Equals(_leaderNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool Renew(string nodeId, TimeSpan leaseDuration)
    {
        lock (_lock)
        {
            if (!string.Equals(_leaderNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            _leaseExpiresUtc = DateTimeOffset.UtcNow.Add(leaseDuration);
            return true;
        }
    }

    public string? CurrentLeader()
    {
        lock (_lock)
        {
            return LeaseExpired() ? null : _leaderNodeId;
        }
    }

    private bool LeaseExpired()
    {
        return !_leaseExpiresUtc.HasValue || _leaseExpiresUtc.Value <= DateTimeOffset.UtcNow;
    }
}
