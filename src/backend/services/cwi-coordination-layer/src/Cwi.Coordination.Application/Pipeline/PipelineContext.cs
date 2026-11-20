namespace Cwi.Coordination.Application.Pipeline;

public sealed class PipelineContext<TRequest>
{
    public TRequest Request { get; }
    public string TraceId { get; }

    public PipelineContext(TRequest request, string traceId)
    {
        Request = request;
        TraceId = traceId;
    }
}
