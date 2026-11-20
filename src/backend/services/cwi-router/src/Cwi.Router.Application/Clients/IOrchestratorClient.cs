using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Clients;

public interface IOrchestratorClient
{
    Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken);
}
