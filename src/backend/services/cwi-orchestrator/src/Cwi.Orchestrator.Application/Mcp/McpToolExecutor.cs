#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Mcp;

public interface IMcpToolExecutor
{
    Task<ServiceResponse<McpExecutionResult>> ExecuteAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken);
}

public sealed class GovernedMcpToolExecutor : IMcpToolExecutor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly IHomeNodeRepository _homeNodeRepository;
    private readonly ApplicationOptions _options;
    private readonly McpConnectorCatalog _connectorCatalog;

    public GovernedMcpToolExecutor(
        IHttpClientFactory httpClientFactory,
        ILedgerRepository ledgerRepository,
        IHomeNodeRepository homeNodeRepository,
        ApplicationOptions options,
        McpConnectorCatalog connectorCatalog)
    {
        _httpClientFactory = httpClientFactory;
        _ledgerRepository = ledgerRepository;
        _homeNodeRepository = homeNodeRepository;
        _options = options;
        _connectorCatalog = connectorCatalog;
    }

    public async Task<ServiceResponse<McpExecutionResult>> ExecuteAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tool.Endpoint))
        {
            return Success(false, "adapter_not_configured", tool, "Tool endpoint is not configured.");
        }

        if (tool.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            tool.Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteHttpAsync(tool, tenantId, userId, traceId, payload, cancellationToken).ConfigureAwait(false);
        }

        if (tool.Endpoint.StartsWith("internal://", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteInternalAsync(tool, tenantId, userId, traceId, payload, cancellationToken).ConfigureAwait(false);
        }

        if (tool.Endpoint.StartsWith("external://", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteExternalAsync(tool, payload, cancellationToken).ConfigureAwait(false);
        }

        if (tool.Endpoint.StartsWith("connector://", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteConnector(tool);
        }

        return Success(false, "adapter_not_configured", tool, $"No MCP adapter is configured for endpoint '{tool.Endpoint}'.");
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteHttpAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("mcp");
            using var request = new HttpRequestMessage(ResolveMethod(tool.Method), tool.Endpoint);
            request.Headers.TryAddWithoutValidation("X-CWI-Tenant-ID", tenantId);
            request.Headers.TryAddWithoutValidation("X-CWI-User-ID", userId);
            request.Headers.TryAddWithoutValidation("X-CWI-Trace-ID", traceId);

            if (!HttpMethod.Get.Equals(request.Method))
            {
                request.Content = JsonContent.Create(payload);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ServiceResponse<McpExecutionResult>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, $"MCP tool '{tool.Name}' failed."),
                    statusCode: 502,
                    message: "MCP tool execution failed");
            }

            return ServiceResponse<McpExecutionResult>.Success(new McpExecutionResult(
                true,
                new Dictionary<string, object?>
                {
                    ["status"] = "executed",
                    ["tool"] = tool.Name,
                    ["endpoint"] = tool.Endpoint,
                    ["response"] = TryParseJson(text)
                }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ServiceResponse<McpExecutionResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, $"MCP tool '{tool.Name}' could not be reached."),
                statusCode: 502,
                message: "MCP tool execution unavailable");
        }
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteInternalAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var internalRoute = tool.Endpoint["internal://".Length..].Trim('/').ToLowerInvariant();
        switch (internalRoute)
        {
            case "system/status":
                return await ExecuteSystemStatusAsync(tool, tenantId, userId, traceId, internalRoute, cancellationToken)
                    .ConfigureAwait(false);

            case "node/health":
                return await ExecuteNodeHealthAsync(tool, tenantId, userId, traceId, internalRoute, cancellationToken)
                    .ConfigureAwait(false);

            case "ledger/view":
                return await ExecuteLedgerViewAsync(tool, traceId, payload, cancellationToken)
                    .ConfigureAwait(false);

            case "packs/list":
                var packsDirectory = _options.PacksDirectory;
                var packs = !string.IsNullOrWhiteSpace(packsDirectory) && Directory.Exists(packsDirectory)
                    ? Directory.EnumerateDirectories(packsDirectory)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Take(200)
                        .ToArray()
                    : Array.Empty<string>();
                return Success(true, "executed", tool, "Pack mount inspected.", new Dictionary<string, object?>
                {
                    ["packs_directory"] = packsDirectory,
                    ["packs"] = packs
                });

            case "packs/get":
                return ExecutePackGet(tool, payload);

            case "pins/resolve":
                const string pinsDirectory = "/srv/cwi/pins";
                var pinsMounted = Directory.Exists(pinsDirectory);
                var pinEntries = pinsMounted
                    ? Directory.EnumerateFileSystemEntries(pinsDirectory)
                        .Select(Path.GetFileName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Take(200)
                        .ToArray()
                    : Array.Empty<string>();
                return Success(pinsMounted, pinsMounted ? "executed" : "adapter_not_configured", tool, pinsMounted
                    ? "Pins mount is available to orchestrator."
                    : "Pins mount is not available to orchestrator.", new Dictionary<string, object?>
                {
                    ["pins_directory"] = pinsDirectory,
                    ["mounted"] = pinsMounted,
                    ["entries"] = pinEntries
                });

            default:
                return Success(false, "adapter_not_configured", tool, $"No internal MCP handler is configured for '{internalRoute}'.");
        }
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteSystemStatusAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        string internalRoute,
        CancellationToken cancellationToken)
    {
        var packMountAvailable = !string.IsNullOrWhiteSpace(_options.PacksDirectory) && Directory.Exists(_options.PacksDirectory);
        var pinsMountAvailable = Directory.Exists("/srv/cwi/pins");
        var ledgerStatus = "up";
        try
        {
            await _ledgerRepository.GetCountAsync(traceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ledgerStatus = "degraded";
        }

        var components = new[]
        {
            ComponentStatus("router", "unknown", "Not directly probed by this read-only MCP tool."),
            ComponentStatus("orchestrator", "up", "This MCP request reached orchestrator."),
            ComponentStatus("pack-service", packMountAvailable ? "up" : "unknown", packMountAvailable ? "Pack mount is readable." : "Pack mount is not visible from orchestrator."),
            ComponentStatus("pin-resolver", pinsMountAvailable ? "up" : "down", pinsMountAvailable ? "Pins mount is readable." : "Pins mount is missing."),
            ComponentStatus("mcp-extension", "up", "Read-only MCP executor is active."),
            ComponentStatus("policy-layer", "up", "Policy was evaluated before this tool executed."),
            ComponentStatus("ledger", ledgerStatus, ledgerStatus == "up" ? "Ledger repository accepted a read check." : "Ledger repository read check failed."),
            ComponentStatus("connector-layer", _connectorCatalog.FindById("__health_check__").ConfigAvailable ? "configured" : "disabled", _connectorCatalog.FindById("__health_check__").ConfigAvailable ? "Connector catalog is available." : "Connector catalog is not configured.")
        };

        return Success(true, "executed", tool, "Component health snapshot generated.", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["trace_id"] = traceId,
            ["mcp_route"] = internalRoute,
            ["observed_utc"] = DateTimeOffset.UtcNow,
            ["components"] = components
        });
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteNodeHealthAsync(
        McpToolDefinition tool,
        string tenantId,
        string userId,
        string traceId,
        string internalRoute,
        CancellationToken cancellationToken)
    {
        var records = await _homeNodeRepository
            .ListAsync(tenantId, ownerUserId: null, status: null, cancellationToken)
            .ConfigureAwait(false);

        var nodes = records
            .OrderBy(record => record.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(record =>
            {
                var state = ResolveNodeState(record.Status, record.LastHeartbeatUtc);
                return new Dictionary<string, object?>
                {
                    ["node_id"] = record.NodeId,
                    ["status"] = state,
                    ["registered_status"] = record.Status,
                    ["tailscale_ip"] = record.TailscaleIp,
                    ["last_heartbeat_utc"] = record.LastHeartbeatUtc,
                    ["last_seen_age_minutes"] = record.LastHeartbeatUtc.HasValue
                        ? Math.Round((DateTimeOffset.UtcNow - record.LastHeartbeatUtc.Value).TotalMinutes, 1)
                        : null
                };
            })
            .ToArray();

        var up = nodes.Count(node => string.Equals(node["status"]?.ToString(), "UP", StringComparison.OrdinalIgnoreCase));
        var down = nodes.Count(node => string.Equals(node["status"]?.ToString(), "DOWN", StringComparison.OrdinalIgnoreCase));
        var unknown = nodes.Count(node => string.Equals(node["status"]?.ToString(), "UNKNOWN", StringComparison.OrdinalIgnoreCase));

        return Success(true, "executed", tool, nodes.Length == 0 ? "No registered CWI nodes were found for this tenant." : "Node health snapshot generated.", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["trace_id"] = traceId,
            ["mcp_route"] = internalRoute,
            ["observed_utc"] = DateTimeOffset.UtcNow,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total"] = nodes.Length,
                ["up"] = up,
                ["down"] = down,
                ["unknown"] = unknown
            },
            ["nodes"] = nodes
        });
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteLedgerViewAsync(
        McpToolDefinition tool,
        string traceId,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var rawScope = FirstString(payload, "scope", "ledger_scope");
        var query = FirstString(payload, "query", "raw_input", "normalized_input") ?? string.Empty;
        var scope = string.IsNullOrWhiteSpace(rawScope) ? InferLedgerScope(query) : rawScope.Trim().ToLowerInvariant();
        IReadOnlyList<Cwi.Orchestrator.Domain.Entities.LedgerEvent> events;
        DateTimeOffset? startUtc = null;
        DateTimeOffset? endUtc = null;

        if (scope is "current_trace" or "trace")
        {
            events = await _ledgerRepository
                .GetByTraceIdAsync(traceId, skip: 0, take: 20, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            endUtc = DateTimeOffset.UtcNow;
            startUtc = scope switch
            {
                "today" => new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero),
                "last_24_hours" or "last24" or "last_24h" => endUtc.Value.AddHours(-24),
                _ => endUtc.Value.AddHours(-24)
            };
            scope = scope is "today" ? "today" : "last_24_hours";
            events = await _ledgerRepository
                .SearchAsync(startUtc, endUtc, "mcp.action", cancellationToken)
                .ConfigureAwait(false);
        }

        var shaped = events
            .OrderByDescending(item => item.CreatedUtc)
            .Take(20)
            .Select(item =>
            {
                var payloadFields = ParseLedgerPayload(item.PayloadJson);
                return new Dictionary<string, object?>
                {
                    ["event_type"] = item.EventType,
                    ["created_utc"] = item.CreatedUtc,
                    ["trace_id"] = item.TraceId,
                    ["action_type"] = payloadFields.ActionType,
                    ["status"] = payloadFields.Status,
                    ["executed"] = payloadFields.Executed,
                    ["hash"] = item.Hash
                };
            })
            .ToArray();

        return Success(true, "executed", tool, "Ledger events retrieved.", new Dictionary<string, object?>
        {
            ["trace_id"] = traceId,
            ["scope"] = scope,
            ["start_utc"] = startUtc,
            ["end_utc"] = endUtc,
            ["events"] = shaped
        });
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteExternalAsync(
        McpToolDefinition tool,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var route = tool.Endpoint["external://".Length..].Trim('/').ToLowerInvariant();
        return route switch
        {
            "weather" => await ExecuteWeatherAsync(tool, payload, cancellationToken).ConfigureAwait(false),
            "web-search" => await ExecuteSearchAsync(tool, payload, cancellationToken).ConfigureAwait(false),
            "news" => await ExecuteSearchAsync(tool, payload, cancellationToken, "latest news").ConfigureAwait(false),
            "sports" => await ExecuteSearchAsync(tool, payload, cancellationToken, "sports score").ConfigureAwait(false),
            _ => Success(false, "adapter_not_configured", tool, $"No external MCP adapter is configured for '{route}'.")
        };
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteWeatherAsync(
        McpToolDefinition tool,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken)
    {
        var location = FirstString(payload, "location", "city", "place");
        var path = string.IsNullOrWhiteSpace(location)
            ? "https://wttr.in/?format=j1"
            : $"https://wttr.in/{Uri.EscapeDataString(location)}?format=j1";

        try
        {
            var client = _httpClientFactory.CreateClient("mcp");
            using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Success(false, "provider_failed", tool, "Weather provider returned an error.", new Dictionary<string, object?>
                {
                    ["provider"] = "wttr.in",
                    ["status_code"] = (int)response.StatusCode
                });
            }

            var parsed = TryParseJson(text);
            return Success(true, "executed", tool, "Weather provider returned forecast data.", new Dictionary<string, object?>
            {
                ["provider"] = "wttr.in",
                ["location"] = string.IsNullOrWhiteSpace(location) ? "auto" : location,
                ["forecast_window"] = FirstString(payload, "forecast_window", "date"),
                ["detail_level"] = FirstString(payload, "detail_level"),
                ["response"] = parsed
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Success(false, "provider_unavailable", tool, "Weather provider is unavailable.");
        }
    }

    private async Task<ServiceResponse<McpExecutionResult>> ExecuteSearchAsync(
        McpToolDefinition tool,
        IReadOnlyDictionary<string, object?> payload,
        CancellationToken cancellationToken,
        string? defaultQueryPrefix = null)
    {
        var query = FirstString(payload, "query", "raw_input", "normalized_input", "question");
        if (string.IsNullOrWhiteSpace(query))
        {
            query = defaultQueryPrefix ?? tool.Name;
        }
        else if (!string.IsNullOrWhiteSpace(defaultQueryPrefix)
                 && !query.Contains(defaultQueryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            query = $"{defaultQueryPrefix} {query}";
        }

        var url = "https://api.duckduckgo.com/?q="
                  + Uri.EscapeDataString(query)
                  + "&format=json&no_html=1&skip_disambig=1";

        try
        {
            var client = _httpClientFactory.CreateClient("mcp");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Success(false, "provider_failed", tool, "Search provider returned an error.", new Dictionary<string, object?>
                {
                    ["provider"] = "duckduckgo_instant_answer",
                    ["query"] = query,
                    ["status_code"] = (int)response.StatusCode
                });
            }

            var parsed = TryParseJson(text);
            var provider = "duckduckgo_instant_answer";
            var results = ExtractSearchResults(parsed);
            if (results.Length == 0)
            {
                var htmlResults = await FetchDuckDuckGoHtmlResultsAsync(client, query, cancellationToken)
                    .ConfigureAwait(false);
                if (htmlResults.Length > 0)
                {
                    provider = "duckduckgo_html";
                    results = htmlResults;
                }
            }

            return Success(true, "executed", tool, "Search provider returned result data.", new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["query"] = query,
                ["response"] = parsed,
                ["results"] = results,
                ["sources"] = results
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Success(false, "provider_unavailable", tool, "Search provider is unavailable.", new Dictionary<string, object?>
            {
                ["provider"] = "duckduckgo_instant_answer",
                ["query"] = query
            });
        }
    }

    private ServiceResponse<McpExecutionResult> ExecuteConnector(McpToolDefinition tool)
    {
        var lookup = _connectorCatalog.FindById(tool.ConnectorId);
        if (!lookup.ConfigAvailable)
        {
            return Success(false, "connector_config_unavailable", tool, lookup.Reason);
        }

        if (lookup.Connector is null)
        {
            return Success(false, "connector_not_configured", tool, lookup.Reason);
        }

        if (!lookup.Connector.Enabled)
        {
            return Success(false, "connector_disabled", tool, lookup.Reason, new Dictionary<string, object?>
            {
                ["connector_id"] = lookup.Connector.Id,
                ["connector_type"] = lookup.Connector.Type
            });
        }

        return Success(false, "connector_adapter_not_wired", tool, $"Connector '{lookup.Connector.Id}' is enabled, but provider execution remains gated until approval.", new Dictionary<string, object?>
        {
            ["connector_id"] = lookup.Connector.Id,
            ["connector_type"] = lookup.Connector.Type
        });
    }

    private ServiceResponse<McpExecutionResult> ExecutePackGet(
        McpToolDefinition tool,
        IReadOnlyDictionary<string, object?> payload)
    {
        var packsDirectory = _options.PacksDirectory;
        if (string.IsNullOrWhiteSpace(packsDirectory) || !Directory.Exists(packsDirectory))
        {
            return Success(false, "adapter_not_configured", tool, "Pack mount is not available to orchestrator.");
        }

        var requestedPack = FirstString(payload, "pack", "pack_name", "pack_id");
        if (string.IsNullOrWhiteSpace(requestedPack))
        {
            return Success(false, "missing_parameter", tool, "A pack name is required for pack.get.", new Dictionary<string, object?>
            {
                ["packs_directory"] = packsDirectory
            });
        }

        var safeName = Path.GetFileName(requestedPack.Trim());
        if (!string.Equals(safeName, requestedPack.Trim(), StringComparison.Ordinal))
        {
            return Success(false, "blocked", tool, "Pack name failed path safety validation.");
        }

        var packPath = Path.Combine(packsDirectory, safeName);
        if (!Directory.Exists(packPath))
        {
            return Success(false, "not_found", tool, $"Pack '{safeName}' was not found.", new Dictionary<string, object?>
            {
                ["pack"] = safeName
            });
        }

        var files = Directory.EnumerateFiles(packPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

        return Success(true, "executed", tool, "Pack metadata inspected.", new Dictionary<string, object?>
        {
            ["pack"] = safeName,
            ["path"] = packPath,
            ["files"] = files
        });
    }

    private static HttpMethod ResolveMethod(string? method)
    {
        return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            ? HttpMethod.Get
            : HttpMethod.Post;
    }

    private static Dictionary<string, object?> ComponentStatus(string name, string status, string detail)
    {
        return new Dictionary<string, object?>
        {
            ["name"] = name,
            ["status"] = status,
            ["detail"] = detail
        };
    }

    private static string ResolveNodeState(string? status, DateTimeOffset? lastHeartbeatUtc)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "offline" or "down" or "disconnected" or "failed")
        {
            return "DOWN";
        }

        if (!lastHeartbeatUtc.HasValue)
        {
            return normalized is "active" or "connected" or "registered" ? "UNKNOWN" : "UNKNOWN";
        }

        var age = DateTimeOffset.UtcNow - lastHeartbeatUtc.Value;
        if (age > TimeSpan.FromMinutes(30))
        {
            return "DOWN";
        }

        return normalized is "active" or "connected" or "registered" or "healthy" or "online"
            ? "UP"
            : "UNKNOWN";
    }

    private static string InferLedgerScope(string query)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("current trace", StringComparison.Ordinal)
            || normalized.Contains("this trace", StringComparison.Ordinal))
        {
            return "current_trace";
        }

        if (normalized.Contains("today", StringComparison.Ordinal))
        {
            return "today";
        }

        return "last_24_hours";
    }

    private static (string? ActionType, string? Status, bool? Executed) ParseLedgerPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return (null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return (
                TryReadString(root, "action_type"),
                TryReadString(root, "status"),
                TryReadBool(root, "executed"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static bool? TryReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, object?>[] ExtractSearchResults(object? parsed)
    {
        if (parsed is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var results = new List<Dictionary<string, object?>>();
        AddSearchResult(
            results,
            TryReadString(element, "Heading"),
            TryReadString(element, "AbstractText"),
            TryReadString(element, "AbstractURL"),
            TryReadString(element, "AbstractSource"));

        if (element.TryGetProperty("Results", out var resultsArray) && resultsArray.ValueKind == JsonValueKind.Array)
        {
            AddRelatedSearchResults(results, resultsArray);
        }

        if (element.TryGetProperty("RelatedTopics", out var relatedTopics) && relatedTopics.ValueKind == JsonValueKind.Array)
        {
            AddRelatedSearchResults(results, relatedTopics);
        }

        return results
            .Where(item => !string.IsNullOrWhiteSpace(item["title"]?.ToString())
                           || !string.IsNullOrWhiteSpace(item["snippet"]?.ToString()))
            .DistinctBy(item => item["url"]?.ToString() ?? item["snippet"]?.ToString() ?? item["title"]?.ToString() ?? string.Empty)
            .Take(5)
            .ToArray();
    }

    private static async Task<Dictionary<string, object?>[]> FetchDuckDuckGoHtmlResultsAsync(
        HttpClient client,
        string query,
        CancellationToken cancellationToken)
    {
        var url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CWI-MCP-Proof/1.0");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ExtractDuckDuckGoHtmlResults(html);
    }

    private static Dictionary<string, object?>[] ExtractDuckDuckGoHtmlResults(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var results = new List<Dictionary<string, object?>>();
        var anchorMatches = Regex.Matches(
            html,
            @"<a\b[^>]*class=""[^""]*result__a[^""]*""[^>]*href=""(?<url>[^""]+)""[^>]*>(?<title>.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        foreach (Match match in anchorMatches)
        {
            var title = CleanHtml(match.Groups["title"].Value);
            var url = NormalizeDuckDuckGoResultUrl(match.Groups["url"].Value);
            var windowLength = Math.Min(2500, html.Length - match.Index);
            var window = windowLength > 0 ? html.Substring(match.Index, windowLength) : string.Empty;
            var snippetMatch = Regex.Match(
                window,
                @"class=""[^""]*result__snippet[^""]*""[^>]*>(?<snippet>.*?)</(?:a|div)>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var snippet = snippetMatch.Success ? CleanHtml(snippetMatch.Groups["snippet"].Value) : null;

            AddSearchResult(results, title, snippet, url, "DuckDuckGo");
            if (results.Count >= 5)
            {
                break;
            }
        }

        return results
            .Where(item => !string.IsNullOrWhiteSpace(item["title"]?.ToString())
                           || !string.IsNullOrWhiteSpace(item["snippet"]?.ToString()))
            .DistinctBy(item => item["url"]?.ToString() ?? item["snippet"]?.ToString() ?? item["title"]?.ToString() ?? string.Empty)
            .Take(5)
            .ToArray();
    }

    private static string? NormalizeDuckDuckGoResultUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var decoded = WebUtility.HtmlDecode(rawUrl.Trim());
        if (decoded.StartsWith("//", StringComparison.Ordinal))
        {
            decoded = "https:" + decoded;
        }

        if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
            && uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2 && string.Equals(pieces[0], "uddg", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
                }
            }
        }

        return decoded;
    }

    private static string? CleanHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var withoutTags = Regex.Replace(value, "<.*?>", " ", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static void AddRelatedSearchResults(List<Dictionary<string, object?>> results, JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (item.TryGetProperty("Topics", out var nested) && nested.ValueKind == JsonValueKind.Array)
            {
                AddRelatedSearchResults(results, nested);
                continue;
            }

            var text = TryReadString(item, "Text") ?? TryReadString(item, "FirstURL");
            var firstUrl = TryReadString(item, "FirstURL");
            var title = SplitSearchTitle(text);
            AddSearchResult(results, title.Title, title.Snippet, firstUrl, TryReadString(item, "Name"));
        }
    }

    private static void AddSearchResult(
        List<Dictionary<string, object?>> results,
        string? title,
        string? snippet,
        string? url,
        string? source)
    {
        if (string.IsNullOrWhiteSpace(title)
            && string.IsNullOrWhiteSpace(snippet)
            && string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        results.Add(new Dictionary<string, object?>
        {
            ["title"] = string.IsNullOrWhiteSpace(title) ? source ?? "Search result" : title.Trim(),
            ["snippet"] = string.IsNullOrWhiteSpace(snippet) ? null : snippet.Trim(),
            ["url"] = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            ["source"] = string.IsNullOrWhiteSpace(source) ? "DuckDuckGo" : source.Trim()
        });
    }

    private static (string? Title, string? Snippet) SplitSearchTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var index = text.IndexOf(" - ", StringComparison.Ordinal);
        if (index <= 0)
        {
            return (text.Trim(), null);
        }

        return (text[..index].Trim(), text[(index + 3)..].Trim());
    }

    private static object? TryParseJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string? FirstString(IReadOnlyDictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            switch (raw)
            {
                case string text when !string.IsNullOrWhiteSpace(text):
                    return text.Trim();
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }

                    break;
            }
        }

        return null;
    }

    private static ServiceResponse<McpExecutionResult> Success(
        bool executed,
        string status,
        McpToolDefinition tool,
        string message,
        Dictionary<string, object?>? extra = null)
    {
        var result = extra is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(extra, StringComparer.OrdinalIgnoreCase);
        result["status"] = status;
        result["tool"] = tool.Name;
        result["endpoint"] = tool.Endpoint;
        result["message"] = message;

        return ServiceResponse<McpExecutionResult>.Success(new McpExecutionResult(executed, result));
    }
}
#nullable disable
