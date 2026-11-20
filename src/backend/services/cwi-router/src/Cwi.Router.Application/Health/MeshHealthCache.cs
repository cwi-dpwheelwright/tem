using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Routing;

namespace Cwi.Router.Application.Health;

public sealed class MeshHealthCache
{
    private readonly MeshAssessmentClient _client;
    private readonly NetworkAssessmentOptions _options;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private MeshHealthSnapshot? _snapshot;
    private DateTimeOffset _updatedUtc;

    public MeshHealthCache(MeshAssessmentClient client, NetworkAssessmentOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<MeshHealthSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.MeshHealthEnabled)
        {
            return _snapshot;
        }

        var now = DateTimeOffset.UtcNow;
        if (_snapshot is not null && now - _updatedUtc <= TimeSpan.FromSeconds(_options.MeshCacheSeconds))
        {
            return _snapshot;
        }

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot is not null && now - _updatedUtc <= TimeSpan.FromSeconds(_options.MeshCacheSeconds))
            {
                return _snapshot;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.MeshTimeoutSeconds));

            var fresh = await _client.GetHealthAsync(timeoutCts.Token).ConfigureAwait(false);
            if (fresh is not null)
            {
                _snapshot = new MeshHealthSnapshot
                {
                    Status = fresh.Status,
                    TotalNodes = fresh.TotalNodes,
                    DegradedNodes = fresh.DegradedNodes,
                    DownNodes = fresh.DownNodes,
                    TotalLinks = fresh.TotalLinks,
                    DegradedLinks = fresh.DegradedLinks,
                    DownLinks = fresh.DownLinks,
                    GeneratedUtc = fresh.GeneratedUtc,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                _updatedUtc = _snapshot.UpdatedUtc;
                return _snapshot;
            }
        }
        catch (Exception)
        {
            // fallback to cached snapshot
        }
        finally
        {
            _mutex.Release();
        }

        return _snapshot;
    }
}
