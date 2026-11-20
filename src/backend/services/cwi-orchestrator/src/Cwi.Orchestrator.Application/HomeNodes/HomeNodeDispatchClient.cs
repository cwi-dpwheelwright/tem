using System.Net.Http.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeDispatchClient
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationOptions _options;

    public HomeNodeDispatchClient(HttpClient httpClient, ApplicationOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.CoordinationBaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.CoordinationAdminToken);

    public bool CanQueryMembership =>
        !string.IsNullOrWhiteSpace(_options.CoordinationBaseUrl);

    internal async Task<ServiceResponse<HomeNodeMembershipSnapshot?>> GetMembershipAsync(
        string nodeId,
        CancellationToken cancellationToken)
    {
        if (!CanQueryMembership)
        {
            return ServiceResponse<HomeNodeMembershipSnapshot?>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination membership is not configured."),
                statusCode: 503,
                message: "coordination membership unavailable");
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return ServiceResponse<HomeNodeMembershipSnapshot?>.Success(null);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, "/api/membership");
        httpRequest.Headers.TryAddWithoutValidation(
            "X-Coordination-Protocol",
            _options.CoordinationProtocolVersion);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.Content is null
                ? $"coordination layer returned {(int)response.StatusCode}"
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeMembershipSnapshot?>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination membership lookup failed."),
                statusCode: (int)response.StatusCode,
                message: message);
        }

        var members = await response.Content.ReadFromJsonAsync<List<HomeNodeMembershipSnapshot>>(
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var membership = members?.FirstOrDefault(member =>
            string.Equals(member.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));

        return ServiceResponse<HomeNodeMembershipSnapshot?>.Success(membership);
    }

    internal async Task<ServiceResponse<HomeNodeWorkReceipt>> CreateAsync(
        CreateHomeNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Home-node dispatch is not configured."),
                statusCode: 503,
                message: "coordination dispatch unavailable");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/node-work")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.TryAddWithoutValidation(
            _options.CoordinationAdminHeaderName,
            _options.CoordinationAdminToken);
        httpRequest.Headers.TryAddWithoutValidation(
            "X-Coordination-Protocol",
            _options.CoordinationProtocolVersion);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.Content is null
                ? $"coordination layer returned {(int)response.StatusCode}"
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Home-node dispatch failed."),
                statusCode: (int)response.StatusCode,
                message: message);
        }

        var payload = await response.Content.ReadFromJsonAsync<HomeNodeWorkReceipt>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.WorkId))
        {
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination layer returned an invalid dispatch receipt."),
                statusCode: 502,
                message: "invalid coordination response");
        }

        return ServiceResponse<HomeNodeWorkReceipt>.Success(payload);
    }

    internal async Task<ServiceResponse<HomeNodeWorkReceipt>> GetAsync(
        string workId,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Home-node dispatch is not configured."),
                statusCode: 503,
                message: "coordination dispatch unavailable");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/node-work/{Uri.EscapeDataString(workId)}");
        httpRequest.Headers.TryAddWithoutValidation(
            _options.CoordinationAdminHeaderName,
            _options.CoordinationAdminToken);
        httpRequest.Headers.TryAddWithoutValidation(
            "X-Coordination-Protocol",
            _options.CoordinationProtocolVersion);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.Content is null
                ? $"coordination layer returned {(int)response.StatusCode}"
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Home-node work status failed."),
                statusCode: (int)response.StatusCode,
                message: message);
        }

        var payload = await response.Content.ReadFromJsonAsync<HomeNodeWorkReceipt>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (payload is null || string.IsNullOrWhiteSpace(payload.WorkId))
        {
            return ServiceResponse<HomeNodeWorkReceipt>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination layer returned an invalid work status."),
                statusCode: 502,
                message: "invalid coordination response");
        }

        return ServiceResponse<HomeNodeWorkReceipt>.Success(payload);
    }
}

public sealed class HomeNodeMembershipSnapshot
{
    [System.Text.Json.Serialization.JsonPropertyName("node_id")]
    public string NodeId { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("owner_user_id")]
    public string? OwnerUserId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("device_class")]
    public string? DeviceClass { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("capabilities")]
    public Dictionary<string, object?> Capabilities { get; init; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("pack_versions")]
    public Dictionary<string, object?> PackVersions { get; init; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("policy_profile_version")]
    public string? PolicyProfileVersion { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("health")]
    public Dictionary<string, object?> Health { get; init; } = new();

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("last_seen_utc")]
    public DateTimeOffset? LastSeenUtc { get; init; }
}
