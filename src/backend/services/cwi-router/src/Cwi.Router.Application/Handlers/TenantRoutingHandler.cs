using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Router.Application.Handlers;

public sealed class TenantRoutingHandler : IRouterHandler
{
    private readonly string? _defaultTenantId;

    [ActivatorUtilitiesConstructor]
    public TenantRoutingHandler(RouterConfig config)
    {
        _defaultTenantId = config.DefaultTenantId;
    }

    public TenantRoutingHandler(string? defaultTenantId = null)
    {
        _defaultTenantId = defaultTenantId;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var resolvedTenantId = context.TenantId;
        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            resolvedTenantId = _defaultTenantId;
        }

        var clientTenantId = TryGetClientTenantId(context);
        if (string.IsNullOrWhiteSpace(resolvedTenantId) && !string.IsNullOrWhiteSpace(clientTenantId))
        {
            resolvedTenantId = clientTenantId;
        }

        if (!string.IsNullOrWhiteSpace(clientTenantId) &&
            !string.IsNullOrWhiteSpace(resolvedTenantId) &&
            !string.Equals(clientTenantId, resolvedTenantId, StringComparison.Ordinal))
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Tenant override not allowed."),
                statusCode: 400,
                message: "Tenant override not allowed"));
        }

        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            var path = context.HttpContext.Request.Path.Value ?? string.Empty;
            if (IsTenantOptional(path))
            {
                return next();
            }

            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Tenant id missing."),
                statusCode: 400,
                message: "Tenant id required"));
        }

        context.TenantId = resolvedTenantId;
        context.HttpContext.Items["tenant_id"] = resolvedTenantId;
        return next();
    }

    private static bool IsTenantOptional(string path)
    {
        return path.StartsWith("/api/break-glass/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/ledger/export", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/trace/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/admin/kill_switch", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/dev/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/survival/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/mesh/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetClientTenantId(RouterRequestContext context)
    {
        var tenantId = context.HttpContext.Request.Headers["x-tenant-id"].ToString();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId;
        }

        tenantId = TryGetTenantIdFromBody(context.Body);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId;
        }

        tenantId = context.HttpContext.Request.Query["tenant_id"].ToString();
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }

    private static string? TryGetTenantIdFromBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (document.RootElement.TryGetProperty("tenant_id", out var tenantProperty) &&
                tenantProperty.ValueKind == JsonValueKind.String)
            {
                return tenantProperty.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
