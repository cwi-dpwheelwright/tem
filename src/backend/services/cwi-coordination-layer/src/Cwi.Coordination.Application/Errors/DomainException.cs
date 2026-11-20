using Cwi.Common.Errors;

namespace Cwi.Coordination.Application.Errors;

public sealed class DomainException : Exception
{
    public DomainError Error { get; }
    public int StatusCode { get; }

    public DomainException(DomainError error, int statusCode, string? message = null) : base(message ?? error.Message)
    {
        Error = error;
        StatusCode = statusCode;
    }
}
