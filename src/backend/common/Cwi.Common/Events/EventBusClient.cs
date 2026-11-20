using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Cwi.Common.Events;

public sealed class EventBusClient
{
    private readonly HttpClient _httpClient;
    private readonly EventBusClientOptions _options;

    public EventBusClient(HttpClient httpClient, EventBusClientOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.Timeout = TimeSpan.FromSeconds(2);
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Url))
        {
            return;
        }

        var payload = envelope with
        {
            EventId = string.IsNullOrWhiteSpace(envelope.EventId)
                ? Guid.NewGuid().ToString("n")
                : envelope.EventId,
            TimestampUtc = envelope.TimestampUtc == default
                ? DateTimeOffset.UtcNow
                : envelope.TimestampUtc,
            Service = string.IsNullOrWhiteSpace(envelope.Service)
                ? _options.ServiceName
                : envelope.Service,
            Environment = string.IsNullOrWhiteSpace(envelope.Environment)
                ? _options.Environment
                : envelope.Environment
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url)
        {
            Content = JsonContent.Create(payload)
        };

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _ = response.EnsureSuccessStatusCode();
        }
        catch
        {
            // Best-effort event publishing; failures should not affect primary flow.
        }
    }
}
