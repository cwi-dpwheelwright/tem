using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Handlers;

public sealed class ActionSchemaValidationHandler : IRouterHandler
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

        var path = request.Path.Value ?? string.Empty;
        if (!path.Equals("/api/action", StringComparison.OrdinalIgnoreCase))
        {
            return next();
        }

        if (string.IsNullOrWhiteSpace(context.Body))
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Action request body is required."),
                statusCode: 400,
                message: "Invalid JSON"));
        }

        try
        {
            using var document = JsonDocument.Parse(context.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Action request must be a JSON object."),
                    statusCode: 400,
                    message: "Invalid JSON"));
            }

            if (!document.RootElement.TryGetProperty("action", out var actionElement) ||
                actionElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Action payload missing."),
                    statusCode: 400,
                    message: "Invalid request"));
            }

            if (!actionElement.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(typeElement.GetString()))
            {
                return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Action type is required."),
                    statusCode: 400,
                    message: "Invalid request"));
            }
        }
        catch (JsonException)
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Action request must be valid JSON."),
                statusCode: 400,
                message: "Invalid JSON"));
        }

        return next();
    }
}
