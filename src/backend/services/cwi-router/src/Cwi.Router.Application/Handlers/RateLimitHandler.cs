using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;
using StackExchange.Redis;

namespace Cwi.Router.Application.Handlers;

public sealed class RateLimitHandler : IRouterHandler
{
    private readonly RouterConfig _config;
    private readonly Lazy<ConnectionMultiplexer> _connection;

    public RateLimitHandler(RouterConfig config)
    {
        _config = config;
        _connection = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(_config.RedisUrl!));
    }

    public async Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (_config.RateLimit <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(_config.RedisUrl))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "REDIS_URL not configured."),
                statusCode: 503,
                message: "Rate limiting unavailable");
        }

        var clientId = context.ClientId ?? context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"rate:{clientId}";

        var db = _connection.Value.GetDatabase();
        var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(_config.RateWindowSeconds)).ConfigureAwait(false);
        }

        if (count > _config.RateLimit)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.RateLimited, "Rate limit exceeded."),
                statusCode: 429,
                message: "Too Many Requests");
        }

        return await next().ConfigureAwait(false);
    }
}

