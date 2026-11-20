#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetryIngestService : DomainServiceBase
{
    private readonly TelemetryIngestWorkflowPipeline _pipeline;
    private readonly TelemetrySchemaValidator _schemaValidator;

    public TelemetryIngestService(
        TelemetryIngestWorkflowPipeline pipeline,
        TelemetrySchemaValidator schemaValidator)
    {
        _pipeline = pipeline;
        _schemaValidator = schemaValidator;
    }

    public Task<ServiceResponse<TelemetryIngestResponseDto>> HandleAsync(
        TelemetryIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = _schemaValidator.Validate(request);
        if (!validation.IsSuccess)
        {
            return Task.FromResult(ServiceResponse<TelemetryIngestResponseDto>.Fail(
                validation.Error ?? new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Telemetry request invalid."),
                statusCode: validation.StatusCode,
                message: validation.Message));
        }

        var context = new TelemetryIngestWorkflowContext(
            request.TenantId,
            request.Window,
            request.Signals,
            request.Metadata);

        return ExecuteAsync(() => _pipeline.ExecuteAsync(context, cancellationToken));
    }
}
#nullable disable
