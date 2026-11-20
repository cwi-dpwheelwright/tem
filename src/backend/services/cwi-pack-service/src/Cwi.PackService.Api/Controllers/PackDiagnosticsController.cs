using Cwi.PackService.Api.Infrastructure;
using Cwi.PackService.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.PackService.Api.Controllers;

[ApiController]
public sealed class PackDiagnosticsController : ControllerBase
{
    private readonly PackDiagnosticsService _diagnosticsService;
    private readonly PackAuthService _authService;

    public PackDiagnosticsController(PackDiagnosticsService diagnosticsService, PackAuthService authService)
    {
        _diagnosticsService = diagnosticsService;
        _authService = authService;
    }

    [HttpGet("/packs/availability")]
    public async Task<IActionResult> GetAvailability()
    {
        var result = await _diagnosticsService.GetAvailabilitySummaryAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return result.ToActionResult();
    }

    [HttpGet("/packs/diagnostics")]
    public async Task<IActionResult> GetDiagnostics()
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: false, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        var result = await _diagnosticsService.GetDiagnosticsAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        return result.ToActionResult();
    }
}

