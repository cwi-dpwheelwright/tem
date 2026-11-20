#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Packs.Ingest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ingest/skillpack")]
public sealed class PackIngestController : ControllerBase
{
    private const long MaxUploadSizeBytes = 100 * 1024 * 1024;
    private readonly PackIngestService _packIngestService;

    public PackIngestController(PackIngestService packIngestService)
    {
        _packIngestService = packIngestService;
    }

    [HttpPost]
    [RequestSizeLimit(MaxUploadSizeBytes)]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackIngestResponseDto>> Ingest(
        [FromForm] PackIngestForm form,
        [FromQuery(Name = "dry_run")] bool? dryRun,
        CancellationToken cancellationToken)
    {
        if (form.Bundle is null || form.Bundle.Length == 0)
        {
            return BadRequest(new DomainError(ErrorCodes.InvalidRequest, "bundle is required."));
        }

        var tempPath = Path.GetTempFileName();
        try
        {
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
            {
                await form.Bundle.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            var request = new PackIngestRequest
            {
                BundlePath = tempPath,
                BundleName = form.Bundle.FileName ?? "bundle",
                DryRun = dryRun ?? form.DryRun ?? false,
                PackIdOverride = form.PackId,
                PackVersionOverride = form.PackVersion,
                Notes = form.Notes,
                TenantId = form.TenantId
            };

            var result = await _packIngestService.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || result.Data is null)
            {
                return StatusCode(result.StatusCode, result.Error);
            }

            return Ok(result.Data);
        }
        finally
        {
            try
            {
                System.IO.File.Delete(tempPath);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    public sealed class PackIngestForm
    {
        [FromForm(Name = "bundle")]
        public IFormFile? Bundle { get; init; }

        [FromForm(Name = "pack_id")]
        public string? PackId { get; init; }

        [FromForm(Name = "pack_version")]
        public string? PackVersion { get; init; }

        [FromForm(Name = "notes")]
        public string? Notes { get; init; }

        [FromForm(Name = "tenant_id")]
        public string? TenantId { get; init; }

        [FromForm(Name = "dry_run")]
        public bool? DryRun { get; init; }
    }
}
#nullable disable
