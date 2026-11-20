using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class MtlsHandler : IRouterHandler
{
    private readonly RouterConfig _config;

    public MtlsHandler(RouterConfig config)
    {
        _config = config;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (!_config.MtlsRequired)
        {
            return next();
        }

        var cert = context.HttpContext.Connection.ClientCertificate;
        if (cert is null)
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Client certificate required."),
                statusCode: 401,
                message: "Unauthorized"));
        }

        return next();
    }
}

