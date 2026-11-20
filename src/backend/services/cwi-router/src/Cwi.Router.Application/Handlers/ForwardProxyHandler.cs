using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class ForwardProxyHandler : IRouterHandler
{
    private readonly IOrchestratorClient _client;
    private readonly RouterConfig _config;
    private readonly OrchestratorCircuitBreaker _breaker;

    public ForwardProxyHandler(IOrchestratorClient client, RouterConfig config, OrchestratorCircuitBreaker breaker)
    {
        _client = client;
        _config = config;
        _breaker = breaker;
    }

    public async Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var endpoint = _config.OrchestratorUrl;
        var now = DateTimeOffset.UtcNow;
        if (_breaker.IsOpen(endpoint, now))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Orchestrator unavailable."),
                statusCode: 503,
                message: "Orchestrator unavailable");
        }

        try
        {
            var response = await _client.ForwardAsync(context, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode >= 500)
            {
                if (AllowsUpstreamFailure(context))
                {
                    return ServiceResponse<RouterResult>.Success(response, response.StatusCode, "OK");
                }

                _breaker.RecordFailure(endpoint, threshold: 3, cooldown: TimeSpan.FromSeconds(30));
                return RouterErrorMapper.FromUpstreamStatus(response.StatusCode);
            }

            _breaker.RecordSuccess(endpoint);
            return ServiceResponse<RouterResult>.Success(response, response.StatusCode, "OK");
        }
        catch (TaskCanceledException ex)
        {
            _breaker.RecordFailure(endpoint, threshold: 3, cooldown: TimeSpan.FromSeconds(30));
            return RouterErrorMapper.FromException(ex);
        }
        catch (Exception ex)
        {
            _breaker.RecordFailure(endpoint, threshold: 3, cooldown: TimeSpan.FromSeconds(30));
            return RouterErrorMapper.FromException(ex);
        }
    }

    private static bool AllowsUpstreamFailure(RouterRequestContext context)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/dev/fault", StringComparison.OrdinalIgnoreCase);
    }
}

