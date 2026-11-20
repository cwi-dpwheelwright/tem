using Cwi.Common.Errors;
using Cwi.Coordination.Application.Errors;
using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Pipeline;

public sealed class MembershipValidationHandler : IPipelineHandler<EnrollmentRequest, EnrollmentResponse>
{
    public Task<EnrollmentResponse> HandleAsync(
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Request.NodeId))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "node_id is required."), 400);
        }
        if (string.IsNullOrWhiteSpace(context.Request.CertificateFingerprint))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "cert_fingerprint is required."), 400);
        }
        return next();
    }
}

public sealed class MembershipAuthnHandler : IPipelineHandler<EnrollmentRequest, EnrollmentResponse>
{
    public Task<EnrollmentResponse> HandleAsync(
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class MembershipAuthzHandler : IPipelineHandler<EnrollmentRequest, EnrollmentResponse>
{
    public Task<EnrollmentResponse> HandleAsync(
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class MembershipStateMachineHandler : IPipelineHandler<EnrollmentRequest, EnrollmentResponse>
{
    public Task<EnrollmentResponse> HandleAsync(
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class MembershipAuditHandler : IPipelineHandler<EnrollmentRequest, EnrollmentResponse>
{
    public Task<EnrollmentResponse> HandleAsync(
        PipelineContext<EnrollmentRequest> context,
        Func<Task<EnrollmentResponse>> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}
