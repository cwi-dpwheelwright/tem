using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Common.Journey;
using Cwi.PackService.Api.Infrastructure;
using Cwi.PackService.Api.Models;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.PackService.Api.Controllers;

[ApiController]
public sealed class PacksController : ControllerBase
{
    private readonly PackDomainService _domain;
    private readonly PackAuthService _authService;
    private readonly PackUploadReader _uploadReader;
    private readonly PackAuditService _auditService;
    private readonly ILogger<PacksController> _logger;
    private readonly JourneyEventBusPublisher _journeyPublisher;

    public PacksController(
        PackDomainService domain,
        PackAuthService authService,
        PackUploadReader uploadReader,
        PackAuditService auditService,
        JourneyEventBusPublisher journeyPublisher,
        ILogger<PacksController> logger)
    {
        _domain = domain;
        _authService = authService;
        _uploadReader = uploadReader;
        _auditService = auditService;
        _journeyPublisher = journeyPublisher;
        _logger = logger;
    }

    [HttpGet("/packs")]
    public Task<IActionResult> GetIndex() => ServeIndexAsync(_domain.GetValidatedIndexAsync(HttpContext.RequestAborted));

    [HttpGet("/packs/index.json")]
    public Task<IActionResult> GetIndexJson() => ServeIndexAsync(_domain.GetValidatedIndexAsync(HttpContext.RequestAborted));

    [HttpGet("/packs/{pack}")]
    public Task<IActionResult> GetPack(string pack)
        => ServeFileAsync(_domain.GetPackJsonAsync(pack, HttpContext.RequestAborted), pack, version: null);

    [HttpGet("/packs/{pack}/pack.json")]
    public Task<IActionResult> GetPackJson(string pack)
        => ServeFileAsync(_domain.GetPackJsonAsync(pack, HttpContext.RequestAborted), pack, version: null);

    [HttpGet("/packs/{pack}/anchor.txt")]
    public Task<IActionResult> GetPackAnchor(string pack)
        => ServeFileAsync(Task.FromResult(_domain.GetAnchor(pack, "anchor.txt")), pack, version: null);

    [HttpGet("/packs/{pack}/{version}/pack.json")]
    public Task<IActionResult> GetVersionedPack(string pack, string version)
        => ServeFileAsync(_domain.GetVersionedPackAsync(pack, version, HttpContext.RequestAborted), pack, version);

    [HttpGet("/packs/{pack}/latest/anchor.txt")]
    public Task<IActionResult> GetLatestAnchor(string pack)
        => ServeFileAsync(Task.FromResult(_domain.GetAnchor(pack, "latest/anchor.txt")), pack, version: "latest");

    [HttpGet("/packs/{pack}/latest/content/{*path}")]
    public Task<IActionResult> GetLatestContent(string pack, string path)
        => ServeFileAsync(Task.FromResult(_domain.GetContentFile(pack, "latest", path)), pack, version: "latest");

    [HttpGet("/packs/{pack}/{version}/content/{*path}")]
    public Task<IActionResult> GetVersionedContent(string pack, string version, string path)
        => ServeFileAsync(Task.FromResult(_domain.GetContentFile(pack, version, path)), pack, version);

    [HttpPost("/packs")]
    public async Task<IActionResult> IngestPack()
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: false, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        PackUploadRequest? request = null;
        try
        {
            var readResult = await _uploadReader.ReadUploadRequestAsync(HttpContext, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (!readResult.IsSuccess)
            {
                return readResult.ToActionResult();
            }

            request = readResult.Data!;
            var result = await _domain.IngestPackAsync(request, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result.IsSuccess && result.Data is { DryRun: false } response)
            {
                _auditService.QueueAudit(new PackAuditEvent
                {
                    EventType = "pack.ingest",
                    PackId = response.PackId,
                    PackVersion = response.PackVersion,
                    Anchor = response.Anchor,
                    Notes = response.Notes
                }, authResult.Data?.ActorId, authResult.Data?.TenantId);
            }

            return result.ToActionResult();
        }
        finally
        {
            if (request is not null && !string.IsNullOrWhiteSpace(request.BundlePath))
            {
                PackUploadReader.TryDeleteTempFile(request.BundlePath, _logger);
            }
        }
    }

    [HttpPatch("/packs/{pack}")]
    public async Task<IActionResult> UpdatePackMetadata(string pack)
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: false, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        string? name = null;
        string? visibility = null;
        bool? retired = null;

        try
        {
            using var doc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: HttpContext.RequestAborted)
                .ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                name = nameElement.GetString();
            }
            if (root.TryGetProperty("visibility", out var visibilityElement) && visibilityElement.ValueKind == JsonValueKind.String)
            {
                visibility = visibilityElement.GetString();
            }
            if (root.TryGetProperty("retired", out var retiredElement) && retiredElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                retired = retiredElement.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return ErrorResult("invalid_request", "Invalid JSON payload.", StatusCodes.Status400BadRequest);
        }

        if (name is null && visibility is null && retired is null)
        {
            return ErrorResult("invalid_request", "No updates provided.", StatusCodes.Status400BadRequest);
        }

        var requireBreakGlass = retired is not null && !authResult.Data!.HasBreakGlass;
        var result = await _domain.UpdatePackMetadataAsync(
                pack,
                name,
                visibility,
                retired,
                requireBreakGlass,
                HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            var eventType = retired is null
                ? "pack.update"
                : (retired.Value ? "pack.retire" : "pack.unretire");

            _auditService.QueueAudit(new PackAuditEvent
            {
                EventType = eventType,
                PackId = pack,
                PackVersion = result.Data?.Latest,
                Notes = null
            }, authResult.Data?.ActorId, authResult.Data?.TenantId);
        }

        return result.ToActionResult();
    }

    [HttpPatch("/packs/{pack}/versions/{version}")]
    public async Task<IActionResult> UpdatePackVersion(string pack, string version)
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: false, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        bool? setLatest = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: HttpContext.RequestAborted)
                .ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("set_latest", out var setLatestElement) &&
                setLatestElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                setLatest = setLatestElement.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return ErrorResult("invalid_request", "Invalid JSON payload.", StatusCodes.Status400BadRequest);
        }

        var result = await _domain.PromotePackVersionAsync(pack, version, setLatest is true, HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _auditService.QueueAudit(new PackAuditEvent
            {
                EventType = "pack.promote",
                PackId = pack,
                PackVersion = version,
                Notes = null
            }, authResult.Data?.ActorId, authResult.Data?.TenantId);
        }

        return result.ToActionResult();
    }

    [HttpDelete("/packs/{pack}")]
    public async Task<IActionResult> DeletePack(string pack)
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: true, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        var result = await _domain.DeletePackAsync(pack, HttpContext.RequestAborted).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _auditService.QueueAudit(new PackAuditEvent
            {
                EventType = "pack.delete",
                PackId = pack,
                PackVersion = result.Data?.Version,
                Notes = null
            }, authResult.Data?.ActorId, authResult.Data?.TenantId);
        }

        return result.ToActionResult();
    }

    [HttpDelete("/packs/{pack}/versions/{version}")]
    public async Task<IActionResult> DeletePackVersion(string pack, string version)
    {
        var authResult = await _authService.EnsureAdminAsync(HttpContext, requireBreakGlass: true, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!authResult.IsSuccess)
        {
            return authResult.ToActionResult();
        }

        var result = await _domain.DeletePackVersionAsync(pack, version, HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _auditService.QueueAudit(new PackAuditEvent
            {
                EventType = "pack.delete_version",
                PackId = pack,
                PackVersion = version,
                Notes = null
            }, authResult.Data?.ActorId, authResult.Data?.TenantId);
        }

        return result.ToActionResult();
    }

    private async Task<IActionResult> ServeFileAsync(
        Task<ServiceResponse<PackFileDescriptor>> responseTask,
        string? pack,
        string? version)
    {
        var started = DateTimeOffset.UtcNow;
        await EmitJourneyAsync(
            "request_received",
            statusCode: null,
            durationMs: null,
            pack,
            version,
            message: null).ConfigureAwait(false);

        var response = await responseTask.ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            await EmitJourneyAsync(
                "response_failed",
                response.StatusCode,
                (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                pack,
                version,
                response.Message).ConfigureAwait(false);
            return response.ToActionResult();
        }

        var descriptor = response.Data!;
        var etag = descriptor.ETag;
        if (!string.IsNullOrWhiteSpace(etag))
        {
            Response.Headers.ETag = etag;
        }
        Response.Headers.CacheControl = "public, max-age=60, must-revalidate";

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(etag) && !string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            var matches = ifNoneMatch.Split(',')
                .Select(value => value.Trim())
                .Any(value => value == etag);
            if (matches)
            {
                await EmitJourneyAsync(
                    "response_ready",
                    StatusCodes.Status304NotModified,
                    (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    pack,
                    version,
                    "Not modified").ConfigureAwait(false);
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        await EmitJourneyAsync(
            "response_ready",
            StatusCodes.Status200OK,
            (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            pack,
            version,
            null).ConfigureAwait(false);
        return PhysicalFile(descriptor.FilePath, descriptor.ContentType);
    }

    private async Task<IActionResult> ServeIndexAsync(Task<ServiceResponse<PackIndex>> responseTask)
    {
        var started = DateTimeOffset.UtcNow;
        await EmitJourneyAsync(
            "request_received",
            statusCode: null,
            durationMs: null,
            pack: null,
            version: null,
            message: null).ConfigureAwait(false);

        var response = await responseTask.ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            await EmitJourneyAsync(
                "response_failed",
                response.StatusCode,
                (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                pack: null,
                version: null,
                response.Message).ConfigureAwait(false);
            return response.ToActionResult();
        }

        var json = JsonSerializer.Serialize(response.Data);
        var etag = ComputeJsonEtag(json);
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "public, max-age=60, must-revalidate";

        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            var matches = ifNoneMatch.Split(',')
                .Select(value => value.Trim())
                .Any(value => value == etag);
            if (matches)
            {
                await EmitJourneyAsync(
                    "response_ready",
                    StatusCodes.Status304NotModified,
                    (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    pack: null,
                    version: null,
                    message: "Not modified").ConfigureAwait(false);
                return StatusCode(StatusCodes.Status304NotModified);
            }
        }

        await EmitJourneyAsync(
            "response_ready",
            StatusCodes.Status200OK,
            (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            pack: null,
            version: null,
            message: null).ConfigureAwait(false);
        return Content(json, "application/json", Encoding.UTF8);
    }

    private Task EmitJourneyAsync(
        string phase,
        int? statusCode,
        long? durationMs,
        string? pack,
        string? version,
        string? message)
    {
        var requestId = Request.Headers["x-request-id"].ToString();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return Task.CompletedTask;
        }

        var traceId = Request.Headers["x-trace-id"].ToString();
        return _journeyPublisher.PublishAsync(new JourneyEvent
        {
            RequestId = requestId,
            TraceId = string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            Service = "cwi-pack-service",
            Component = "pack_service",
            Phase = phase,
            Direction = phase.StartsWith("response", StringComparison.OrdinalIgnoreCase) ? "response" : "request",
            FromComponent = phase.StartsWith("response", StringComparison.OrdinalIgnoreCase) ? "cwi-pack-service" : "cwi-orchestrator",
            ToComponent = phase.StartsWith("response", StringComparison.OrdinalIgnoreCase) ? "cwi-orchestrator" : "cwi-pack-service",
            TenantId = Request.Headers["x-tenant-id"].ToString(),
            UserId = Request.Headers["x-user-id"].ToString(),
            PackName = pack ?? Request.Headers["x-pack-name"].ToString(),
            Method = Request.Method,
            Path = Request.Path.Value,
            StatusCode = statusCode,
            DurationMs = durationMs,
            Message = message,
            Metadata = new Dictionary<string, object?>
            {
                ["version"] = version
            }
        }, HttpContext.RequestAborted);
    }

    private static IActionResult ErrorResult(string code, string message, int statusCode)
    {
        return new ObjectResult(new DomainError(code, message))
        {
            StatusCode = statusCode
        };
    }

    private static string ComputeJsonEtag(string json)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"\"{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }
}
