using System.Collections.Concurrent;
using Cwi.Router.Application.Configuration;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Health;

public sealed class ModelEndpointRegistry
{
    private readonly IReadOnlyList<Uri> _proEndpoints;
    private readonly IReadOnlyList<Uri> _freeEndpoints;
    private readonly ConcurrentDictionary<string, ModelEndpointProbeStatus> _status =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModelEndpointCapabilities> _capabilities =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, Uri> _capabilityEndpoints;
    private readonly ModelEndpointCircuitBreaker _breaker;
    private readonly RouterConfig _config;
    private readonly ILogger<ModelEndpointRegistry> _logger;

    public ModelEndpointRegistry(
        RouterConfig config,
        ModelEndpointCircuitBreaker breaker,
        ILogger<ModelEndpointRegistry> logger)
    {
        _config = config;
        _breaker = breaker;
        _logger = logger;
        _proEndpoints = ParseEndpoints(config.ModelProEndpoints, "MODEL_PRO_URLS");
        _freeEndpoints = ParseEndpoints(config.ModelFreeEndpoints, "MODEL_FREE_URLS");
        var capabilityMap = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in BuildCapabilitiesMap(
                     _proEndpoints,
                     config.ModelProCapabilitiesEndpoints,
                     "MODEL_PRO_CAPABILITIES_URLS"))
        {
            capabilityMap[entry.Key] = entry.Value;
        }
        foreach (var entry in BuildCapabilitiesMap(
                     _freeEndpoints,
                     config.ModelFreeCapabilitiesEndpoints,
                     "MODEL_FREE_CAPABILITIES_URLS"))
        {
            capabilityMap[entry.Key] = entry.Value;
        }
        _capabilityEndpoints = capabilityMap;
    }

    public IReadOnlyList<Uri> GetEndpoints(ModelTier tier)
    {
        return tier == ModelTier.Pro ? _proEndpoints : _freeEndpoints;
    }

    public IReadOnlyList<Uri> GetAllEndpoints()
    {
        if (_proEndpoints.Count == 0 && _freeEndpoints.Count == 0)
        {
            return Array.Empty<Uri>();
        }

        return _proEndpoints
            .Concat(_freeEndpoints)
            .Distinct(UriComparer.Instance)
            .ToArray();
    }

    public IReadOnlyList<Uri> GetHealthyEndpoints(ModelTier tier, DateTimeOffset now)
    {
        var endpoints = GetEndpoints(tier);
        if (endpoints.Count == 0)
        {
            return Array.Empty<Uri>();
        }

        return endpoints
            .Where(endpoint => !_breaker.IsOpen(endpoint.ToString(), now))
            .Where(endpoint => IsCapabilityReady(endpoint))
            .ToArray();
    }

    public bool IsHealthy(Uri endpoint, DateTimeOffset now)
    {
        return !_breaker.IsOpen(endpoint.ToString(), now);
    }

    public void RecordSuccess(Uri endpoint, DateTimeOffset now)
    {
        _breaker.RecordSuccess(endpoint.ToString());
        _status.AddOrUpdate(
            endpoint.ToString(),
            _ => new ModelEndpointProbeStatus { LastCheckedUtc = now, LastSuccessUtc = now },
            (_, status) => status.WithSuccess(now));
    }

    public void RecordFailure(Uri endpoint, DateTimeOffset now, string detail)
    {
        _breaker.RecordFailure(
            endpoint.ToString(),
            _config.ModelCircuitBreakerFailureThreshold,
            TimeSpan.FromSeconds(_config.ModelCircuitBreakerCooldownSeconds));
        _status.AddOrUpdate(
            endpoint.ToString(),
            _ => new ModelEndpointProbeStatus { LastCheckedUtc = now, Detail = detail },
            (_, status) => status.WithFailure(now, detail));
    }

    public bool TryGetStatus(Uri endpoint, out ModelEndpointProbeStatus status)
    {
        return _status.TryGetValue(endpoint.ToString(), out status!);
    }

    public bool TryGetCapabilitiesEndpoint(Uri endpoint, out Uri capabilitiesEndpoint)
    {
        return _capabilityEndpoints.TryGetValue(endpoint.ToString(), out capabilitiesEndpoint!);
    }

    public bool TryGetCapabilities(Uri endpoint, out ModelEndpointCapabilities capabilities)
    {
        return _capabilities.TryGetValue(endpoint.ToString(), out capabilities!);
    }

    public void RecordCapabilities(Uri endpoint, ModelEndpointCapabilities capabilities)
    {
        _capabilities.AddOrUpdate(endpoint.ToString(), capabilities, (_, _) => capabilities);
    }

    private IReadOnlyList<Uri> ParseEndpoints(IReadOnlyList<string> raw, string source)
    {
        if (raw.Count == 0)
        {
            return Array.Empty<Uri>();
        }

        var endpoints = new List<Uri>(raw.Count);
        foreach (var entry in raw)
        {
            if (Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                endpoints.Add(uri);
            }
            else
            {
                _logger.LogWarning("Ignoring invalid model endpoint from {Source}: {Endpoint}", source, entry);
            }
        }

        if (endpoints.Count <= 1)
        {
            return endpoints;
        }

        return endpoints.Distinct(UriComparer.Instance).ToArray();
    }

    private IReadOnlyDictionary<string, Uri> BuildCapabilitiesMap(
        IReadOnlyList<Uri> endpoints,
        IReadOnlyList<string> capabilityEndpoints,
        string source)
    {
        if (capabilityEndpoints.Count == 0 || endpoints.Count == 0)
        {
            return new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        }

        if (capabilityEndpoints.Count != endpoints.Count)
        {
            _logger.LogWarning(
                "{Source} count {CapabilityCount} does not match model endpoint count {EndpointCount}.",
                source,
                capabilityEndpoints.Count,
                endpoints.Count);
        }

        var map = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Min(capabilityEndpoints.Count, endpoints.Count);
        for (var i = 0; i < limit; i++)
        {
            var raw = capabilityEndpoints[i];
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            {
                map[endpoints[i].ToString()] = uri;
            }
            else
            {
                _logger.LogWarning("Ignoring invalid capabilities endpoint from {Source}: {Endpoint}", source, raw);
            }
        }

        return map;
    }

    private bool IsCapabilityReady(Uri endpoint)
    {
        if (!_capabilities.TryGetValue(endpoint.ToString(), out var capabilities))
        {
            return true;
        }

        return capabilities.Ready;
    }

    private sealed class UriComparer : IEqualityComparer<Uri>
    {
        public static UriComparer Instance { get; } = new();

        public bool Equals(Uri? x, Uri? y)
        {
            return string.Equals(x?.ToString(), y?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(Uri obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ToString());
        }
    }
}
