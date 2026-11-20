#nullable enable
using System.Diagnostics;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Action;

public sealed class ActionService : DomainServiceBase
{
    private readonly ActionWorkflowPipeline _pipeline;
    private readonly ApplicationOptions _options;

    public ActionService(ActionWorkflowPipeline pipeline, ApplicationOptions options)
    {
        _pipeline = pipeline;
        _options = options;
    }

    public Task<ServiceResponse<ActionResponseDto>> HandleAsync(
        ActionRequestDto request,
        CancellationToken cancellationToken)
    {
        var context = new ActionWorkflowContext(
            request.TenantId,
            request.UserId,
            request.PolicyMode,
            request.Action.Type,
            request.Action.Payload);
        context.ModelName = string.IsNullOrWhiteSpace(request.ModelId) ? _options.ModelName : request.ModelId;
        context.PolicyVersion = string.IsNullOrWhiteSpace(request.PolicyVersion) ? _options.PolicyVersion : request.PolicyVersion;
        context.PackHash = request.PackHash;
        context.PackVersion = request.PackVersion;
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            context.TraceId = traceId;
        }

        return ExecuteAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(context.PolicyVersion))
            {
                return Task.FromResult(ServiceResponse<ActionResponseDto>.Fail(
                    new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Policy version is required."),
                    statusCode: 400,
                    message: "Policy version missing"));
            }

            if (string.IsNullOrWhiteSpace(context.ModelName))
            {
                return Task.FromResult(ServiceResponse<ActionResponseDto>.Fail(
                    new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Model identifier is required."),
                    statusCode: 400,
                    message: "Model identifier missing"));
            }

            if (string.IsNullOrWhiteSpace(context.ActionType))
            {
                return Task.FromResult(ServiceResponse<ActionResponseDto>.Fail(
                    new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Action type is required."),
                    statusCode: 400,
                    message: "Action type missing"));
            }

            return _pipeline.ExecuteAsync(context, cancellationToken);
        });
    }
}
#nullable disable
