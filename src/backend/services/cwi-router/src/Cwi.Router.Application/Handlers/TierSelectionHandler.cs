using System.Text;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Handlers;

public sealed class TierSelectionHandler : IRouterHandler
{
    private readonly RouterConfig _config;
    private readonly ModelEndpointRegistry _registry;
    private readonly ILogger<TierSelectionHandler> _logger;

    public TierSelectionHandler(
        RouterConfig config,
        ModelEndpointRegistry registry,
        ILogger<TierSelectionHandler> logger)
    {
        _config = config;
        _registry = registry;
        _logger = logger;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (context.ModelUrl is not null)
        {
            return next();
        }

        var requestedTier = ParseTier(context.HttpContext.Request.Headers["x-model-tier"].ToString());
        var selection = SelectEndpoint(context, requestedTier, DateTimeOffset.UtcNow);

        if (!selection.IsSuccess)
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, selection.Detail),
                statusCode: 503,
                message: "Model endpoint unavailable"));
        }

        if (selection.FallbackUsed)
        {
            _logger.LogWarning(
                "model tier fallback selected requested={RequestedTier} selected={SelectedTier} tenant_id={TenantId} trace_id={TraceId}",
                requestedTier,
                selection.SelectedTier,
                context.TenantId ?? "unknown",
                context.HttpContext.TraceIdentifier);
        }

        context.ModelUrl = selection.Endpoint;
        return next();
    }

    private SelectionResult SelectEndpoint(
        RouterRequestContext context,
        ModelTier requestedTier,
        DateTimeOffset now)
    {
        var seed = context.TenantId ?? context.HttpContext.TraceIdentifier;

        foreach (var tier in GetFallbackOrder(requestedTier))
        {
            var endpoints = _registry.GetHealthyEndpoints(tier, now);
            if (endpoints.Count == 0)
            {
                continue;
            }

            var index = GetStableIndex(seed, endpoints.Count, tier);
            return SelectionResult.Success(endpoints[index], tier, tier != requestedTier);
        }

        var detail = requestedTier == ModelTier.Pro
            ? "MODEL_PRO_URL(S) not configured or no healthy endpoints available."
            : "MODEL_FREE_URL(S) not configured or no healthy endpoints available.";
        return SelectionResult.Fail(detail);
    }

    private static IEnumerable<ModelTier> GetFallbackOrder(ModelTier requestedTier)
    {
        yield return requestedTier;
        yield return requestedTier == ModelTier.Pro ? ModelTier.Free : ModelTier.Pro;
    }

    private static ModelTier ParseTier(string? tier)
    {
        return string.Equals(tier, "pro", StringComparison.OrdinalIgnoreCase)
            ? ModelTier.Pro
            : ModelTier.Free;
    }

    private static int GetStableIndex(string seed, int count, ModelTier tier)
    {
        var material = $"{tier}:{seed}";
        var hash = ComputeFnv1a32(material);
        return (int)(hash % (uint)count);
    }

    private static uint ComputeFnv1a32(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        var bytes = Encoding.UTF8.GetBytes(value);
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private sealed record SelectionResult(
        bool IsSuccess,
        Uri? Endpoint,
        ModelTier SelectedTier,
        bool FallbackUsed,
        string Detail)
    {
        public static SelectionResult Success(Uri endpoint, ModelTier selectedTier, bool fallbackUsed)
        {
            return new SelectionResult(true, endpoint, selectedTier, fallbackUsed, string.Empty);
        }

        public static SelectionResult Fail(string detail)
        {
            return new SelectionResult(false, null, ModelTier.Free, false, detail);
        }
    }
}

