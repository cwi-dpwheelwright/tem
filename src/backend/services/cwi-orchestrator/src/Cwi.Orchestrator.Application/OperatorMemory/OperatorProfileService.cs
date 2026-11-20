#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorProfileService : DomainServiceBase
{
    private readonly IOperatorProfileRepository _profileRepository;
    private readonly IMemoryCache _cache;
    private readonly OperatorMemoryOptions _options;

    public OperatorProfileService(
        IOperatorProfileRepository profileRepository,
        IMemoryCache cache,
        OperatorMemoryOptions options)
    {
        _profileRepository = profileRepository;
        _cache = cache;
        _options = options;
    }

    public Task<ServiceResponse<OperatorProfileSummaryDto>> GetAsync(
        string tenantId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleAsync(tenantId, operatorId, cancellationToken));
    }

    private async Task<ServiceResponse<OperatorProfileSummaryDto>> HandleAsync(
        string tenantId,
        string operatorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(operatorId))
        {
            return ServiceResponse<OperatorProfileSummaryDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and operator_id are required."),
                statusCode: 400,
                message: "tenant_id and operator_id required");
        }

        var cacheKey = $"operator_profile::{tenantId.Trim().ToLowerInvariant()}::{operatorId.Trim().ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out OperatorProfileSummaryDto? cached) && cached is not null)
        {
            return ServiceResponse<OperatorProfileSummaryDto>.Success(cached);
        }

        var profile = await _profileRepository
            .GetAsync(tenantId.Trim(), operatorId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return ServiceResponse<OperatorProfileSummaryDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "profile not found."),
                statusCode: 404,
                message: "profile not found");
        }

        var summary = OperatorProfileCalculator.ToSummary(profile, _options);
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.ProfileCacheSeconds)
        };
        _cache.Set(cacheKey, summary, cacheOptions);
        return ServiceResponse<OperatorProfileSummaryDto>.Success(summary);
    }
}
#nullable disable
