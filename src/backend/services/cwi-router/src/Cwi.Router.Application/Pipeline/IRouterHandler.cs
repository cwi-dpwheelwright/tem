using Cwi.Common;

namespace Cwi.Router.Application.Pipeline;

public interface IRouterHandler
{
    Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken);
}
