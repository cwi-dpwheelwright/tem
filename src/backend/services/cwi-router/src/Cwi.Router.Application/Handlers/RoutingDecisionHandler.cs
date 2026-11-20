using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;
using Cwi.Router.Application.Routing;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Cwi.Router.Application.Handlers;

public sealed class RoutingDecisionHandler : IRouterHandler
{
    private readonly ComplexityClassifier _classifier;
    private readonly RoutingDecisionEngine _engine;
    private readonly RoutingDecisionOptions _options;
    private readonly NetworkAssessmentService _networkAssessmentService;
    private readonly RouteAttestationService _attestationService;
    private readonly IEnumerable<IRoutingDecisionSink> _sinks;
    private readonly ILogger<RoutingDecisionHandler> _logger;

    public RoutingDecisionHandler(
        ComplexityClassifier classifier,
        RoutingDecisionEngine engine,
        RoutingDecisionOptions options,
        NetworkAssessmentService networkAssessmentService,
        RouteAttestationService attestationService,
        IEnumerable<IRoutingDecisionSink> sinks,
        ILogger<RoutingDecisionHandler> logger)
    {
        _classifier = classifier;
        _engine = engine;
        _options = options;
        _networkAssessmentService = networkAssessmentService;
        _attestationService = attestationService;
        _sinks = sinks;
        _logger = logger;
    }

    public async Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (!_classifier.TryClassify(path, context.Body, out var descriptor))
        {
            return await next().ConfigureAwait(false);
        }

        var requestedTier = ParseTier(context.HttpContext.Request.Headers["x-model-tier"].ToString());
        var networkAssessment = await _networkAssessmentService
            .EvaluateAsync(context, descriptor, cancellationToken)
            .ConfigureAwait(false);
        context.NetworkAssessment = networkAssessment;

        var decision = _engine.Decide(descriptor, requestedTier, DateTimeOffset.UtcNow, networkAssessment);
        context.RoutingDecision = decision;

        var attestation = _attestationService.Create(decision, context);
        context.RouteAttestation = attestation;
        context.HttpContext.Request.Headers["X-Route-Attestation"] = _attestationService.Serialize(attestation);

        if (_options.OverrideTierWhenMissing && string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers["x-model-tier"]))
        {
            context.HttpContext.Request.Headers["x-model-tier"] = decision.SelectedTier.ToString().ToLowerInvariant();
        }

        if (decision.SelectedEndpoint is not null)
        {
            context.ModelUrl = decision.SelectedEndpoint;
            context.HttpContext.Request.Headers["x-model-endpoint"] = decision.SelectedEndpoint.ToString();
        }

        foreach (var sink in _sinks)
        {
            await sink.RecordAsync(decision, context, cancellationToken).ConfigureAwait(false);
        }

        var response = await next().ConfigureAwait(false);

        try
        {
            context.HttpContext.Response.Headers["X-Routing-Decision-Id"] = decision.DecisionId;
            context.HttpContext.Response.Headers["X-Routing-Selected-Tier"] = decision.SelectedTier.ToString().ToLowerInvariant();
            context.HttpContext.Response.Headers["X-Routing-Complexity"] = decision.Complexity.Level.ToString().ToLowerInvariant();
            if (decision.NetworkAssessment?.Score is not null)
            {
                context.HttpContext.Response.Headers["X-Routing-Network-Score"] =
                    decision.NetworkAssessment.Score.Value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrWhiteSpace(decision.NetworkAssessment?.Status))
            {
                context.HttpContext.Response.Headers["X-Routing-Network-Status"] = decision.NetworkAssessment.Status;
            }

            if (!string.IsNullOrWhiteSpace(decision.NetworkAssessment?.MeshHealth?.Status))
            {
                context.HttpContext.Response.Headers["X-Routing-Mesh-Health"] = decision.NetworkAssessment.MeshHealth.Status;
            }

            if (!string.IsNullOrWhiteSpace(decision.NetworkAssessment?.PreferredInterface))
            {
                context.HttpContext.Response.Headers["X-Routing-Preferred-Interface"] =
                    decision.NetworkAssessment.PreferredInterface;
            }

            if (decision.NetworkAssessment is not null)
            {
                context.HttpContext.Response.Headers["X-Routing-Reroute-Required"] =
                    decision.RerouteRequired ? "true" : "false";
                if (!string.IsNullOrWhiteSpace(decision.RerouteReason))
                {
                    context.HttpContext.Response.Headers["X-Routing-Reroute-Reason"] = decision.RerouteReason;
                }
            }

            if (attestation is not null)
            {
                context.HttpContext.Response.Headers["X-Route-Attestation-Id"] = attestation.AttestationId;
            }
            if (decision.ReasonCodes.Count > 0)
            {
                context.HttpContext.Response.Headers["X-Routing-Reasons"] = string.Join(',', decision.ReasonCodes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "unable to set routing decision headers");
        }

        return response;
    }

    private static ModelTier ParseTier(string? tier)
    {
        return string.Equals(tier, "pro", StringComparison.OrdinalIgnoreCase)
            ? ModelTier.Pro
            : ModelTier.Free;
    }
}
