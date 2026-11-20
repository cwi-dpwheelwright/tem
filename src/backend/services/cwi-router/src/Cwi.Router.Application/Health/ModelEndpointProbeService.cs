using System.Text.Json;
using Cwi.Router.Application.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Health;

public sealed class ModelEndpointProbeService : BackgroundService
{
    private readonly ModelEndpointRegistry _registry;
    private readonly RouterConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelEndpointProbeService> _logger;

    public ModelEndpointProbeService(
        ModelEndpointRegistry registry,
        RouterConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<ModelEndpointProbeService> logger)
    {
        _registry = registry;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_config.ModelHealthProbeIntervalSeconds);
        if (interval <= TimeSpan.Zero)
        {
            _logger.LogInformation("Model health probe disabled (interval <= 0).");
            return;
        }

        await ProbeOnceAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ProbeOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProbeOnceAsync(CancellationToken cancellationToken)
    {
        var endpoints = _registry.GetAllEndpoints();
        if (endpoints.Count == 0)
        {
            return;
        }

        var client = _httpClientFactory.CreateClient("model-probe");

        foreach (var endpoint in endpoints)
        {
            var now = DateTimeOffset.UtcNow;
            var probeUrl = new Uri(endpoint, _config.ModelHealthPath);
            try
            {
                var response = await client.GetAsync(probeUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _registry.RecordSuccess(endpoint, now);
                    await ProbeCapabilitiesAsync(endpoint, client, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                _registry.RecordFailure(endpoint, now, $"HTTP {(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _registry.RecordFailure(endpoint, now, ex.GetType().Name);
            }
        }
    }

    private async Task ProbeCapabilitiesAsync(
        Uri endpoint,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        if (!_registry.TryGetCapabilitiesEndpoint(endpoint, out var capabilitiesEndpoint))
        {
            return;
        }

        try
        {
            var response = await client.GetAsync(capabilitiesEndpoint, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            if (!TryParseCapabilities(payload, endpoint, out var capabilities))
            {
                return;
            }

            _registry.RecordCapabilities(endpoint, capabilities);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "capabilities probe failed for {Endpoint}", endpoint);
        }
    }

    private static bool TryParseCapabilities(
        string payload,
        Uri endpoint,
        out ModelEndpointCapabilities capabilities)
    {
        capabilities = new ModelEndpointCapabilities();

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var engine = GetString(root, "engine");
            var tier = GetString(root, "tier");
            var ready = GetBool(root, "ready") ?? true;
            var maxConcurrency = GetInt(root, "capacity", "max_concurrency");
            var queueDepth = GetInt(root, "capacity", "queue_depth");
            var inferenceMode = ParseInferenceMode(root, endpoint, engine);

            capabilities = new ModelEndpointCapabilities
            {
                Engine = engine,
                Tier = tier,
                Ready = ready,
                MaxConcurrency = maxConcurrency,
                QueueDepth = queueDepth,
                InferenceMode = inferenceMode
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static InferenceEngineMode ParseInferenceMode(JsonElement root, Uri endpoint, string? engine)
    {
        var explicitMode = GetString(root, "inference_mode");
        if (!string.IsNullOrWhiteSpace(explicitMode))
        {
            if (string.Equals(explicitMode, "local", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "edge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "on_device", StringComparison.OrdinalIgnoreCase))
            {
                return InferenceEngineMode.Local;
            }

            if (string.Equals(explicitMode, "remote", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "cloud", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(explicitMode, "managed", StringComparison.OrdinalIgnoreCase))
            {
                return InferenceEngineMode.Remote;
            }
        }

        var localFlag = GetBool(root, "local_inference");
        if (localFlag.HasValue)
        {
            return localFlag.Value ? InferenceEngineMode.Local : InferenceEngineMode.Remote;
        }

        if (!string.IsNullOrWhiteSpace(engine))
        {
            if (engine.Contains("ollama", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("local", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("edge", StringComparison.OrdinalIgnoreCase))
            {
                return InferenceEngineMode.Local;
            }

            if (engine.Contains("openai", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("azure", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("bedrock", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
                engine.Contains("cloud", StringComparison.OrdinalIgnoreCase))
            {
                return InferenceEngineMode.Remote;
            }
        }

        if (endpoint.IsLoopback)
        {
            return InferenceEngineMode.Local;
        }

        return InferenceEngineMode.Unknown;
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return element.GetString();
    }

    private static bool? GetBool(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static int? GetInt(JsonElement root, string parent, string property)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object ||
            !parentElement.TryGetProperty(property, out var child))
        {
            return null;
        }

        if (child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var value))
        {
            return value;
        }

        if (child.ValueKind == JsonValueKind.String && int.TryParse(child.GetString(), out value))
        {
            return value;
        }

        return null;
    }
}
