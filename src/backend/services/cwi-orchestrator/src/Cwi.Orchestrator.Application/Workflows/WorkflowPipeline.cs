#nullable enable
using Cwi.Common;

namespace Cwi.Orchestrator.Application.Workflows;

public delegate Task<ServiceResponse<TResult>> WorkflowStepDelegate<TResult>();

public interface IWorkflowStep<TContext, TResult>
{
    Task<ServiceResponse<TResult>> ExecuteAsync(
        TContext context,
        WorkflowStepDelegate<TResult> next,
        CancellationToken cancellationToken);
}

public interface IWorkflowDecorator<TContext, TResult>
{
    Task<ServiceResponse<TResult>> ExecuteAsync(
        TContext context,
        WorkflowStepDelegate<TResult> next,
        CancellationToken cancellationToken);
}

public class WorkflowPipeline<TContext, TResult>
{
    private readonly IReadOnlyList<IWorkflowStep<TContext, TResult>> _steps;
    private readonly IReadOnlyList<IWorkflowDecorator<TContext, TResult>> _decorators;

    public WorkflowPipeline(IEnumerable<IWorkflowStep<TContext, TResult>> steps)
    {
        _steps = steps.ToList();
        _decorators = Array.Empty<IWorkflowDecorator<TContext, TResult>>();
    }

    public WorkflowPipeline(
        IEnumerable<IWorkflowStep<TContext, TResult>> steps,
        IEnumerable<IWorkflowDecorator<TContext, TResult>> decorators)
    {
        _steps = steps.ToList();
        _decorators = decorators.ToList();
    }

    public Task<ServiceResponse<TResult>> ExecuteAsync(
        TContext context,
        CancellationToken cancellationToken)
    {
        return ExecuteDecoratorAsync(0, context, cancellationToken);
    }

    private Task<ServiceResponse<TResult>> ExecuteDecoratorAsync(
        int index,
        TContext context,
        CancellationToken cancellationToken)
    {
        if (index >= _decorators.Count)
        {
            return ExecuteStepAsync(0, context, cancellationToken);
        }

        var decorator = _decorators[index];
        return decorator.ExecuteAsync(
            context,
            () => ExecuteDecoratorAsync(index + 1, context, cancellationToken),
            cancellationToken);
    }

    private Task<ServiceResponse<TResult>> ExecuteStepAsync(
        int index,
        TContext context,
        CancellationToken cancellationToken)
    {
        if (index >= _steps.Count)
        {
            throw new InvalidOperationException("No terminal workflow step registered.");
        }

        var step = _steps[index];
        return step.ExecuteAsync(
            context,
            () => ExecuteStepAsync(index + 1, context, cancellationToken),
            cancellationToken);
    }
}
#nullable disable
