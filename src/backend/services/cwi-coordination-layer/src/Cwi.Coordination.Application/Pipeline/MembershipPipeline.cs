using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Pipeline;

public sealed class MembershipPipeline
{
    private readonly IReadOnlyList<IPipelineHandler<EnrollmentRequest, EnrollmentResponse>> _handlers;

    public MembershipPipeline(IEnumerable<IPipelineHandler<EnrollmentRequest, EnrollmentResponse>> handlers)
    {
        _handlers = handlers.ToList();
    }

    public Task<EnrollmentResponse> ExecuteAsync(
        EnrollmentRequest request,
        string traceId,
        Func<Task<EnrollmentResponse>> terminal,
        CancellationToken cancellationToken)
    {
        var context = new PipelineContext<EnrollmentRequest>(request, traceId);
        return ExecuteAtAsync(0, context, terminal, cancellationToken);
    }

    private Task<EnrollmentResponse> ExecuteAtAsync(
        int index,
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> terminal,
        CancellationToken cancellationToken)
    {
        if (index >= _handlers.Count)
        {
            return terminal();
        }
        return _handlers[index].HandleAsync(
            context,
            () => ExecuteAtAsync(index + 1, context, terminal, cancellationToken),
            cancellationToken);
    }
}
