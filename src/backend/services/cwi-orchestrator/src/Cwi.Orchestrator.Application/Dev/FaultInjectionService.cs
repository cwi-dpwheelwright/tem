using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Dev;

public sealed class FaultInjectionService : DomainServiceBase
{
    public Task<ServiceResponse<FaultInjectionResult>> InjectAsync(string? kind, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var mode = (kind ?? string.Empty).Trim().ToLowerInvariant();

        if (string.Equals(mode, "domain", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ServiceResponse<FaultInjectionResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Injected domain error."),
                statusCode: 422,
                message: "Injected domain error"));
        }

        if (string.Equals(mode, "success", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ServiceResponse<FaultInjectionResult>.Success(new FaultInjectionResult
            {
                Status = "ok",
                Detail = "Injected success"
            }));
        }

        return ExecuteAsync<FaultInjectionResult>(() =>
            Task.FromException<FaultInjectionResult>(new InvalidOperationException("Injected fault")));
    }
}

public sealed class FaultInjectionResult
{
    public string Status { get; init; } = "error";
    public string? Detail { get; init; }
}
