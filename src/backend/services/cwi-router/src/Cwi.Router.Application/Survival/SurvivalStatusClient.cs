using System.Net.Http.Json;
using Cwi.Router.Application.Configuration;

namespace Cwi.Router.Application.Survival;

public sealed class SurvivalStatusClient
{
    private readonly HttpClient _client;
    private readonly RouterConfig _config;

    public SurvivalStatusClient(IHttpClientFactory httpClientFactory, RouterConfig config)
    {
        _client = httpClientFactory.CreateClient("orchestrator");
        _config = config;
    }

    public async Task<SurvivalState?> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(new Uri(_config.OrchestratorUrl), "/api/survival/status");
            var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SurvivalStatusPayload>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload is null)
            {
                return null;
            }

            return new SurvivalState
            {
                Mode = string.IsNullOrWhiteSpace(payload.Mode) ? "normal" : payload.Mode,
                Profile = payload.Profile,
                LastSignalUtc = payload.LastSignalUtc,
                UpdatedUtc = DateTimeOffset.UtcNow,
                Source = "orchestrator"
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class SurvivalStatusPayload
    {
        public string? Mode { get; init; }
        public string? Profile { get; init; }
        public DateTimeOffset? LastSignalUtc { get; init; }
    }
}
