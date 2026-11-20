using Cwi.Common;

namespace Cwi.Router.Application.Pipeline;

public sealed class RouterPipeline
{
    private readonly IReadOnlyList<IRouterHandler> _handlers;

    public RouterPipeline(IEnumerable<IRouterHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    public Task<ServiceResponse<RouterResult>> ExecuteAsync(
        RouterRequestContext context,
        CancellationToken cancellationToken)
    {
        return ExecuteHandlerAsync(0, context, cancellationToken);
    }

    private Task<ServiceResponse<RouterResult>> ExecuteHandlerAsync(
        int index,
        RouterRequestContext context,
        CancellationToken cancellationToken)
    {
        if (index >= _handlers.Count)
        {
            throw new InvalidOperationException("No terminal handler registered.");
        }

        var handler = _handlers[index];
        return handler.HandleAsync(
            context,
            () => ExecuteHandlerAsync(index + 1, context, cancellationToken),
            cancellationToken);
    }
}
