using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class ModelHealthCircuitBreakerHandler : IRouterHandler
{
    private readonly ModelEndpointRegistry _registry;

    public ModelHealthCircuitBreakerHandler(
        ModelEndpointRegistry registry)
    {
        _registry = registry;
    }

    public async Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (context.ModelUrl is null)
        {
            return await next().ConfigureAwait(false);
        }

        var now = DateTimeOffset.UtcNow;
        if (!_registry.IsHealthy(context.ModelUrl, now))
        {
            var detail = _registry.TryGetStatus(context.ModelUrl, out var status)
                ? status.Detail ?? "Model endpoint unhealthy."
                : "Model endpoint unhealthy.";
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, detail),
                statusCode: 503,
                message: "Model endpoint unavailable");
        }

        return await next().ConfigureAwait(false);
    }
}

