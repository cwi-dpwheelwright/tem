using System.Text.Json;
using Cwi.Common.Events;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Cwi.Orchestrator.Api.Journey;

public sealed class JourneyStreamIngestHostedService : BackgroundService
{
    private readonly JourneyStreamOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JourneyBroadcaster _broadcaster;
    private readonly ILogger<JourneyStreamIngestHostedService> _logger;
    private readonly object _connectionGate = new();
    private Task<ConnectionMultiplexer>? _connectionTask;

    public JourneyStreamIngestHostedService(
        JourneyStreamOptions options,
        IServiceScopeFactory scopeFactory,
        JourneyBroadcaster broadcaster,
        ILogger<JourneyStreamIngestHostedService> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.RedisUrl))
        {
            _logger.LogInformation("Journey stream ingest disabled.");
            return;
        }

        var lastId = await LoadStartingStreamIdAsync(stoppingToken).ConfigureAwait(false) ?? "$";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await GetConnectionAsync().ConfigureAwait(false);
                var database = connection.GetDatabase();
                var result = await database.ExecuteAsync(
                    "XREAD",
                    "COUNT",
                    _options.BatchSize,
                    "BLOCK",
                    _options.BlockMs,
                    "STREAMS",
                    _options.Stream,
                    lastId).ConfigureAwait(false);

                var items = ParseResult(result);
                if (items.Count == 0)
                {
                    continue;
                }

                foreach (var item in items)
                {
                    lastId = item.StreamId;
                    if (!JourneyEventEnvelopeConverter.TryParse(item.Event, out var journeyEvent))
                    {
                        continue;
                    }

                    if (await PersistAsync(item.StreamId, journeyEvent, stoppingToken).ConfigureAwait(false))
                    {
                        _broadcaster.Publish(journeyEvent);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ResetConnection();
                _logger.LogWarning(ex, "Journey stream ingest failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
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

    private async Task<string?> LoadStartingStreamIdAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ingestService = scope.ServiceProvider.GetRequiredService<JourneyStreamIngestService>();
        return await ingestService.GetStartingStreamIdAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PersistAsync(string streamId, JourneyEvent journeyEvent, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ingestService = scope.ServiceProvider.GetRequiredService<JourneyStreamIngestService>();
        return await ingestService.PersistIfNewAsync(streamId, journeyEvent, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<EventStreamItem> ParseResult(RedisResult result)
    {
        if (result.IsNull || result.Length == 0)
        {
            return Array.Empty<EventStreamItem>();
        }

        var items = new List<EventStreamItem>();
        for (var streamIndex = 0; streamIndex < result.Length; streamIndex++)
        {
            var stream = result[streamIndex];
            if (stream.IsNull || stream.Length < 2)
            {
                continue;
            }

            var streamEntries = stream[1];
            if (streamEntries.IsNull || streamEntries.Length == 0)
            {
                continue;
            }

            for (var i = 0; i < streamEntries.Length; i++)
            {
                var entry = streamEntries[i];
                if (entry.IsNull || entry.Length < 2)
                {
                    continue;
                }

                var streamId = entry[0].ToString() ?? string.Empty;
                var fields = entry[1];
                var payload = ExtractField(fields, "payload");
                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                try
                {
                    var envelope = JsonSerializer.Deserialize<EventEnvelope>(payload);
                    if (envelope is not null)
                    {
                        items.Add(new EventStreamItem(streamId, envelope));
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        return items;
    }

    private static string? ExtractField(RedisResult fields, string name)
    {
        if (fields.IsNull || fields.Length <= 1)
        {
            return null;
        }

        for (var i = 0; i + 1 < fields.Length; i += 2)
        {
            if (string.Equals(fields[i].ToString(), name, StringComparison.Ordinal))
            {
                return fields[i + 1].ToString();
            }
        }

        return null;
    }

    private sealed record EventStreamItem(string StreamId, EventEnvelope Event);
}
