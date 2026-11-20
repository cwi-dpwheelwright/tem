using System.Net.Http;

namespace Cwi.Coordination.Api.Health;

public sealed class DependencyHealthChecker
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration _configuration;

    public DependencyHealthChecker(IHttpClientFactory clientFactory, IConfiguration configuration)
    {
        _clientFactory = clientFactory;
        _configuration = configuration;
    }

    public async Task<DependencyHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        var urls = _configuration.GetSection("Dependencies:Urls").Get<string[]>() ?? Array.Empty<string>();
        if (urls.Length == 0)
        {
            return new DependencyHealthResult(true, Array.Empty<DependencyStatus>());
        }

        var client = _clientFactory.CreateClient();
        var statuses = new List<DependencyStatus>();
        var allHealthy = true;
        foreach (var url in urls)
        {
            try
            {
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                var ok = response.IsSuccessStatusCode;
                statuses.Add(new DependencyStatus(url, ok));
                if (!ok)
                {
                    allHealthy = false;
                }
            }
            catch
            {
                statuses.Add(new DependencyStatus(url, false));
                allHealthy = false;
            }
        }

        return new DependencyHealthResult(allHealthy, statuses);
    }
}

public sealed record DependencyStatus(string Url, bool Healthy);

public sealed record DependencyHealthResult(bool Ready, IReadOnlyList<DependencyStatus> Dependencies);
