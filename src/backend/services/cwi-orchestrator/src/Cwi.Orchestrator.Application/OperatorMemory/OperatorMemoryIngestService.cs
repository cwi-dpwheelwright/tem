#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorMemoryIngestService : DomainServiceBase
{
    private readonly IOperatorProfileRepository _profileRepository;
    private readonly OperatorMemoryRedactionPolicy _redactionPolicy;
    private readonly OperatorMemoryRateLimiter _rateLimiter;
    private readonly OperatorMemoryOptions _options;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public OperatorMemoryIngestService(
        IOperatorProfileRepository profileRepository,
        OperatorMemoryRedactionPolicy redactionPolicy,
        OperatorMemoryRateLimiter rateLimiter,
        OperatorMemoryOptions options,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _profileRepository = profileRepository;
        _redactionPolicy = redactionPolicy;
        _rateLimiter = rateLimiter;
        _options = options;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<OperatorMemoryIngestResponseDto>> HandleAsync(
        OperatorMemoryIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleInternalAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<OperatorMemoryIngestResponseDto>> HandleInternalAsync(
        OperatorMemoryIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.OperatorId))
        {
            return ServiceResponse<OperatorMemoryIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and operator_id are required."),
                statusCode: 400,
                message: "tenant_id and operator_id required");
        }

        if (request.Consent is null || request.Consent.Granted is false)
        {
            return ServiceResponse<OperatorMemoryIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "consent required"),
                statusCode: 403,
                message: "consent required");
        }

        var sanitized = _redactionPolicy.Sanitize(request);
        var totalEvents = sanitized.Signals.Count + sanitized.Performance.Count;
        if (totalEvents == 0)
        {
            return ServiceResponse<OperatorMemoryIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "signals or performance required"),
                statusCode: 400,
                message: "signals or performance required");
        }

        var limiterKey = $"{sanitized.TenantId}:{sanitized.OperatorId}";
        if (!_rateLimiter.TryConsume(limiterKey, totalEvents, out var retryAfter))
        {
            return ServiceResponse<OperatorMemoryIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.RateLimited, "rate limit exceeded"),
                statusCode: 429,
                message: "rate limit exceeded");
        }

        var existing = await _profileRepository
            .GetAsync(sanitized.TenantId.Trim(), sanitized.OperatorId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        var updated = OperatorProfileCalculator.ApplyBatch(existing, sanitized, _options);
        await _profileRepository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        var profile = OperatorProfileCalculator.ToSummary(updated, _options);
        await AppendLedgerEventAsync(sanitized, profile, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<OperatorMemoryIngestResponseDto>.Success(new OperatorMemoryIngestResponseDto
        {
            BatchId = Guid.NewGuid().ToString("N"),
            Accepted = totalEvents,
            Rejected = 0,
            Status = "accepted",
            Profile = profile
        });
    }

    private async Task AppendLedgerEventAsync(
        OperatorMemoryIngestRequestDto request,
        OperatorProfileSummaryDto profile,
        CancellationToken cancellationToken)
    {
        var traceId = $"operator_memory:{request.TenantId}:{request.OperatorId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = request.TenantId,
            operator_id = request.OperatorId,
            signal_count = request.Signals.Count,
            performance_count = request.Performance.Count,
            correlation_score = profile.CorrelationScore,
            updated_utc = profile.LastUpdatedUtc
        });

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "operator_memory.ingest_batch",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
