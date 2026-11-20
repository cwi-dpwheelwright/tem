using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class RequestValidationHandler : IRouterHandler
{
    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return next();
        }

        if (string.IsNullOrWhiteSpace(context.Body))
        {
            return next();
        }

        var contentType = request.ContentType ?? string.Empty;
        if (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase) && contentType.Length > 0)
        {
            return next();
        }

        try
        {
            using var document = JsonDocument.Parse(context.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Request body must be a JSON object."),
                    statusCode: 400,
                    message: "Invalid JSON"));
            }
        }
        catch (JsonException)
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Request body must be valid JSON."),
                statusCode: 400,
                message: "Invalid JSON"));
        }

        return next();
    }
}
