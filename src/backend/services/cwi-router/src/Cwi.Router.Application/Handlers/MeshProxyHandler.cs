using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class MeshProxyHandler : IRouterHandler
{
    private readonly ICoordinationClient _client;
    private readonly RouterConfig _config;
    private readonly CoordinationCircuitBreaker _breaker;

    public MeshProxyHandler(
        ICoordinationClient client,
        RouterConfig config,
        CoordinationCircuitBreaker breaker)
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
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/mesh/", StringComparison.OrdinalIgnoreCase))
        {
            return await next().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(_config.CoordinationLayerUrl))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination layer not configured."),
                statusCode: 503,
                message: "Coordination layer not configured");
        }

        var endpoint = _config.CoordinationLayerUrl;
        var now = DateTimeOffset.UtcNow;
        if (_breaker.IsOpen(endpoint, now))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Coordination layer unavailable."),
                statusCode: 503,
                message: "Coordination layer unavailable");
        }

        try
        {
            var response = await _client.ForwardAsync(context, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode >= 500)
            {
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
}
