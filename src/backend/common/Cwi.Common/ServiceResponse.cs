#nullable enable
using Cwi.Common.Errors;

namespace Cwi.Common;

public sealed class ServiceResponse<T>
{
    public bool IsSuccess { get; }
    public int StatusCode { get; }
    public string Message { get; }
    public T? Data { get; }
    public DomainError? Error { get; }

    private ServiceResponse(bool isSuccess, int statusCode, string message, T? data, DomainError? error)
    {
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        Message = message;
        Data = data;
        Error = error;
    }

    public static ServiceResponse<T> Success(T data, int statusCode = 200, string message = "OK")
    {
        return new ServiceResponse<T>(true, statusCode, message, data, null);
    }

    public static ServiceResponse<T> Fail(DomainError error, int statusCode, string message)
    {
        return new ServiceResponse<T>(false, statusCode, message, default, error);
    }
}
#nullable disable
