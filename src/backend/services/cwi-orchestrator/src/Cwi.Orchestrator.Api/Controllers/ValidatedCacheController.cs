#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/validated-cache")]
public sealed class ValidatedCacheController : ControllerBase
{
    private readonly ValidatedCacheService _service;

    public ValidatedCacheController(ValidatedCacheService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<object>> Upsert(
        [FromBody] ValidatedCacheEntryDto request,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        await _service.UpsertAsync(request, cancellationToken).ConfigureAwait(false);

        return Ok(new
        {
            accepted = true,
            cache_key = request.CacheKey,
            trace_id = request.TraceId
        });
    }

    [HttpGet("evaluate")]
    public async Task<ActionResult<ValidatedCacheEvaluationDto>> Evaluate(
        [FromQuery(Name = "tenant_id")] string tenantId,
        [FromQuery(Name = "cache_key")] string cacheKey,
        [FromQuery(Name = "user_id_hash")] string? userIdHash,
        [FromQuery(Name = "policy_mode")] string policyMode,
        [FromQuery(Name = "policy_version")] string? policyVersion,
        [FromQuery(Name = "pack_hash")] string packHash,
        [FromQuery(Name = "now_utc")] DateTimeOffset? nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service.EvaluateAsync(
            tenantId,
            cacheKey,
            new ValidatedCacheLookupContextDto
            {
                TenantId = tenantId,
                UserIdHash = userIdHash,
                PolicyMode = policyMode,
                PolicyVersion = policyVersion,
                PackHash = packHash,
                NowUtc = nowUtc ?? DateTimeOffset.UtcNow
            },
            cancellationToken).ConfigureAwait(false);

        return Ok(result);
    }
}
#nullable disable
