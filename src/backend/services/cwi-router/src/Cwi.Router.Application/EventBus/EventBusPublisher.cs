using System.Text.Json;
using Cwi.Common.Events;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cwi.Router.Application.EventBus;

public sealed class EventBusPublisher
{
    private readonly EventBusOptions _options;
    private readonly ILogger<EventBusPublisher> _logger;
    private readonly object _connectionGate = new();
    private Task<ConnectionMultiplexer>? _connectionTask;
    private long _lastTrimUnixMs;

    public EventBusPublisher(EventBusOptions options, ILogger<EventBusPublisher> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.RedisUrl))
        {
            return;
        }

        IDatabase? db;
        try
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);
            db = connection.GetDatabase();
        }
        catch (Exception ex)
        {
            ResetConnection();
            _logger.LogWarning(ex, "Event bus unavailable.");
            return;
        }

        var payload = envelope with
        {
            EventId = string.IsNullOrWhiteSpace(envelope.EventId)
                ? Guid.NewGuid().ToString("n")
                : envelope.EventId,
            TimestampUtc = envelope.TimestampUtc == default
                ? DateTimeOffset.UtcNow
                : envelope.TimestampUtc
        };

        var serialized = JsonSerializer.Serialize(payload);
        var entries = new[]
        {
            new NameValueEntry("payload", serialized),
            new NameValueEntry("service", payload.Service ?? string.Empty),
            new NameValueEntry("type", payload.Type ?? string.Empty),
            new NameValueEntry("polarity", payload.Polarity ?? string.Empty),
            new NameValueEntry("severity", payload.Severity ?? string.Empty),
            new NameValueEntry("tenant_id", payload.TenantId ?? string.Empty)
        };

        try
        {
            await db.StreamAddAsync(_options.Stream, entries).ConfigureAwait(false);
            await MaybeTrimAsync(db).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ResetConnection();
            _logger.LogWarning(ex, "Event bus publish failed.");
        }
    }

    private Task<ConnectionMultiplexer> GetConnectionAsync()
    {
        lock (_connectionGate)
        {
            _connectionTask ??= ConnectAsync();
            return _connectionTask;
        }
    }

    private void ResetConnection()
    {
        lock (_connectionGate)
        {
            _connectionTask = null;
        }
    }

    private Task<ConnectionMultiplexer> ConnectAsync()
    {
        var options = ConfigurationOptions.Parse(_options.RedisUrl);
        options.AbortOnConnectFail = false;
        options.ConnectRetry = Math.Max(options.ConnectRetry, 3);
        return ConnectionMultiplexer.ConnectAsync(options);
    }

    private async Task MaybeTrimAsync(IDatabase db)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lastTrimMs = Interlocked.Read(ref _lastTrimUnixMs);
        if (lastTrimMs != 0 && nowMs - lastTrimMs < 60_000)
        {
            return;
        }

        Interlocked.Exchange(ref _lastTrimUnixMs, nowMs);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays).ToUnixTimeMilliseconds();
        var minId = $"{cutoff}-0";

        try
        {
            await db.ExecuteAsync("XTRIM", _options.Stream, "MINID", "~", minId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event bus trim failed.");
        }
    }
}
