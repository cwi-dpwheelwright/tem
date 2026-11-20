using Cwi.Router.Application.Pipeline;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Clients;

public sealed class OrchestratorClientLoggingDecorator : IOrchestratorClient
{
    private readonly IOrchestratorClient _inner;
    private readonly ILogger<OrchestratorClientLoggingDecorator> _logger;

    public OrchestratorClientLoggingDecorator(
        IOrchestratorClient inner,
        ILogger<OrchestratorClientLoggingDecorator> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
    {
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        var method = context.HttpContext.Request.Method;

        try
        {
            var result = await _inner.ForwardAsync(context, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "orchestrator_forward method={Method} path={Path} status={StatusCode}",
                method,
                path,
                result.StatusCode);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "orchestrator_forward_failed method={Method} path={Path}",
                method,
                path);
            throw;
        }
    }
}
