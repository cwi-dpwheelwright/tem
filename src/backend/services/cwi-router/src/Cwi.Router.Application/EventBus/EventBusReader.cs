using System.Text.Json;
using Cwi.Common.Events;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cwi.Router.Application.EventBus;

public sealed record EventStreamItem(string StreamId, EventEnvelope Event);

public sealed class EventBusReader
{
    private readonly EventBusOptions _options;
    private readonly ILogger<EventBusReader> _logger;
    private readonly object _connectionGate = new();
    private Task<ConnectionMultiplexer>? _connectionTask;

    public EventBusReader(EventBusOptions options, ILogger<EventBusReader> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EventStreamItem>> ReadLatestAsync(string? beforeId, int limit)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.RedisUrl))
        {
            return Array.Empty<EventStreamItem>();
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
            return Array.Empty<EventStreamItem>();
        }

        var start = string.IsNullOrWhiteSpace(beforeId) ? "+" : beforeId;
        RedisResult result;
        try
        {
            result = await db.ExecuteAsync("XREVRANGE", _options.Stream, start, "-", "COUNT", limit).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ResetConnection();
            _logger.LogWarning(ex, "Event bus read failed.");
            return Array.Empty<EventStreamItem>();
        }

        return ParseResult(result);
    }

    private static IReadOnlyList<EventStreamItem> ParseResult(RedisResult result)
    {
        if (result.IsNull)
        {
            return Array.Empty<EventStreamItem>();
        }

        var entryCount = result.Length;
        if (entryCount <= 0)
        {
            return Array.Empty<EventStreamItem>();
        }

        var items = new List<EventStreamItem>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var entry = result[i];
            if (entry.IsNull || entry.Length < 2)
            {
                continue;
            }

            var streamId = entry[0].ToString();
            var fields = entry[1];
            if (fields.IsNull || fields.Length < 2)
            {
                continue;
            }

            var payload = ExtractField(fields, "payload");
            EventEnvelope? envelope = null;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    envelope = JsonSerializer.Deserialize<EventEnvelope>(payload);
                }
                catch
                {
                }
            }

            envelope ??= new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString("n"),
                TimestampUtc = DateTimeOffset.UtcNow,
                Service = ExtractField(fields, "service") ?? "unknown",
                Type = ExtractField(fields, "type") ?? "event",
                Polarity = ExtractField(fields, "polarity") ?? "positive",
                Severity = ExtractField(fields, "severity") ?? "info",
                TenantId = ExtractField(fields, "tenant_id")
            };

            items.Add(new EventStreamItem(streamId ?? string.Empty, envelope));
        }

        return items;
    }

    private static string? ExtractField(RedisResult fields, string name)
    {
        var length = fields.Length;
        if (length <= 1)
        {
            return null;
        }

        for (var i = 0; i + 1 < length; i += 2)
        {
            if (string.Equals(fields[i].ToString(), name, StringComparison.Ordinal))
            {
                return fields[i + 1].ToString();
            }
        }

        return null;
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
}
