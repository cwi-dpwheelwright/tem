#nullable enable
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/account/connectors/oauth/callback")]
public sealed class ConnectorOAuthCallbackController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromQuery(Name = "error")] string? error)
    {
        var status = string.IsNullOrWhiteSpace(error) ? "returned" : "error";
        return Redirect($"/settings?connector_auth={status}");
    }
}
#nullable disable
