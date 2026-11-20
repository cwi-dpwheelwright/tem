using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public static class RouterErrorMapper
{
    public static ServiceResponse<RouterResult> FromUpstreamStatus(int statusCode)
    {
        if (statusCode >= 500)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Upstream error."),
                statusCode: 503,
                message: "Service unavailable");
        }

        return ServiceResponse<RouterResult>.Fail(
            new DomainError(ErrorCodes.InternalError, "Unexpected upstream status."),
            statusCode: 500,
            message: "Unexpected upstream status");
    }

    public static ServiceResponse<RouterResult> FromException(Exception ex)
    {
        if (ex is TaskCanceledException)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Upstream timeout."),
                statusCode: 504,
                message: "Gateway timeout");
        }

        return ServiceResponse<RouterResult>.Fail(
            new DomainError(ErrorCodes.DependencyUnavailable, "Upstream request failed."),
            statusCode: 503,
            message: "Service unavailable");
    }
}
