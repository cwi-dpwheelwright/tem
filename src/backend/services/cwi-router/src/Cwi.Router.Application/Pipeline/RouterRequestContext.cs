using Microsoft.AspNetCore.Http;

namespace Cwi.Router.Application.Pipeline;

public sealed class RouterRequestContext
{
    public HttpContext HttpContext { get; }
    public string Body { get; }
    public string? ClientId { get; set; }
    public string? TenantId { get; set; }
    public string? PolicyMode { get; set; }
    public Uri? ModelUrl { get; set; }
    public Cwi.Router.Application.Routing.RoutingDecision? RoutingDecision { get; set; }
    public Cwi.Router.Application.Routing.NetworkAssessmentSnapshot? NetworkAssessment { get; set; }
    public Cwi.Router.Application.Routing.RouteAttestationEnvelope? RouteAttestation { get; set; }
    public Cwi.Router.Application.Routing.RouteAttestationEnvelope? InboundRouteAttestation { get; set; }

    public RouterRequestContext(HttpContext httpContext, string body)
    {
        HttpContext = httpContext;
        Body = body;
    }
}
