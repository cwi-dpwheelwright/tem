using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Pipeline;
using Cwi.Router.Application.Routing;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Handlers;

public sealed class RouteAttestationValidationHandler : IRouterHandler
{
    private readonly RouteAttestationValidator _validator;
    private readonly ILogger<RouteAttestationValidationHandler> _logger;

    public RouteAttestationValidationHandler(
        RouteAttestationValidator validator,
        ILogger<RouteAttestationValidationHandler> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        var header = context.HttpContext.Request.Headers["X-Route-Attestation"].ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return next();
        }

        RouteAttestationEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RouteAttestationEnvelope>(header);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "invalid route attestation header");
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Invalid route attestation header."),
                statusCode: 400,
                message: "Invalid route attestation"));
        }

        if (envelope is null)
        {
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Missing route attestation payload."),
                statusCode: 400,
                message: "Invalid route attestation"));
        }

        if (!_validator.TryValidate(envelope, out var reason))
        {
            _logger.LogWarning(
                "route attestation rejected reason={Reason} trace_id={TraceId}",
                reason ?? "unknown",
                context.HttpContext.TraceIdentifier);
            return Task.FromResult(ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Route attestation failed validation."),
                statusCode: 409,
                message: "Route attestation rejected"));
        }

        context.InboundRouteAttestation = envelope;
        return next();
    }
}
