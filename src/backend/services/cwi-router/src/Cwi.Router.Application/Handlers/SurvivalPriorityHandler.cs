using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Metrics;
using Cwi.Router.Application.Pipeline;
using Cwi.Router.Application.Survival;

namespace Cwi.Router.Application.Handlers;

public sealed class SurvivalPriorityHandler : IRouterHandler
{
    private readonly SurvivalStateStore _store;
    private readonly RouterMetrics _metrics;

    public SurvivalPriorityHandler(SurvivalStateStore store, RouterMetrics metrics)
    {
        _store = store;
        _metrics = metrics;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/ask", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("/api/action", StringComparison.OrdinalIgnoreCase))
        {
            return next();
        }

        var state = _store.GetSnapshot();
        if (!string.Equals(state.Mode, "survival", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(state.Mode, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            return next();
        }

        var priority = context.HttpContext.Request.Headers["x-priority"].ToString();
        if (string.Equals(priority, "high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(priority, "critical", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.RecordSurvivalPriorityAllowed();
            return next();
        }

        _metrics.RecordSurvivalPriorityBlocked();
        return Task.FromResult(ServiceResponse<RouterResult>.Fail(
            new DomainError(ErrorCodes.Forbidden, "Request deprioritized during survival mode."),
            statusCode: 503,
            message: "Survival mode priority gate"));
    }
}
