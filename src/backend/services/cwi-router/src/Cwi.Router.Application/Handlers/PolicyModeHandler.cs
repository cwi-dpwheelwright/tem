using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class PolicyModeHandler : IRouterHandler
{
    private readonly RouterConfig _config;

    public PolicyModeHandler(RouterConfig config)
    {
        _config = config;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var header = context.HttpContext.Request.Headers["x-policy-mode"].ToString();
        var policy = string.IsNullOrWhiteSpace(header) ? _config.PolicyModeDefault : header;

        if (string.IsNullOrWhiteSpace(policy))
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Policy mode missing."),
                statusCode: 400,
                message: "Policy mode required"));
        }

        context.PolicyMode = policy;
        return next();
    }
}

