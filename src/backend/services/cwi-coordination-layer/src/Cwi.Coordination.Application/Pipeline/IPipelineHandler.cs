namespace Cwi.Coordination.Application.Pipeline;

public interface IPipelineHandler<TRequest, TResponse>
{
    Task<TResponse> HandleAsync(PipelineContext<TRequest> context, Func<Task<TResponse>> next, CancellationToken cancellationToken);
}
