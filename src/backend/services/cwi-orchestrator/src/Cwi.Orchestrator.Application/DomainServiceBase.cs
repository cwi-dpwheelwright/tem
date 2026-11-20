using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application;

public abstract class DomainServiceBase
{
    protected async Task<ServiceResponse<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            return ServiceResponse<T>.Success(result);
        }
        catch (Exception ex)
        {
            return ServiceResponse<T>.Fail(
                new DomainError(ErrorCodes.InternalError, ex.GetType().Name),
                statusCode: 500,
                message: "Unhandled domain service error");
        }
    }

    protected async Task<ServiceResponse<T>> ExecuteAsync<T>(Func<Task<ServiceResponse<T>>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ServiceResponse<T>.Fail(
                new DomainError(ErrorCodes.InternalError, ex.GetType().Name),
                statusCode: 500,
                message: "Unhandled domain service error");
        }
    }
}
