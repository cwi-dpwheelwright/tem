using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using System.Linq;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Feedback;

public interface IOutcomeFeedbackService
{
    Task<ServiceResponse<OutcomeFeedbackResponseDto>> RecordAsync(
        OutcomeFeedbackRequestDto request,
        CancellationToken cancellationToken);

    Task<ServiceResponse<OutcomeFeedbackSummaryDto>> GetSummaryAsync(
        string tenantId,
        string traceId,
        CancellationToken cancellationToken);
}

public sealed class OutcomeFeedbackService : IOutcomeFeedbackService
{
    private readonly IOutcomeFeedbackRepository _repository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;
    private readonly OutcomeFeedbackSignalStore _signalStore;

    public OutcomeFeedbackService(
        IOutcomeFeedbackRepository repository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory,
        OutcomeFeedbackSignalStore signalStore)
    {
        _repository = repository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
        _signalStore = signalStore;
    }

    public async Task<ServiceResponse<OutcomeFeedbackResponseDto>> RecordAsync(
        OutcomeFeedbackRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.TraceId))
        {
            return ServiceResponse<OutcomeFeedbackResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and trace_id are required."),
                statusCode: 400,
                message: "Invalid request");
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResponse<OutcomeFeedbackResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "actor_id is required."),
                statusCode: 400,
                message: "Invalid request");
        }

        if (!request.ConsentGranted)
        {
            return ServiceResponse<OutcomeFeedbackResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "consent is required to record feedback."),
                statusCode: 403,
                message: "Consent required");
        }

        var feedback = NormalizeFeedback(request.Feedback);
        if (feedback is null)
        {
            return ServiceResponse<OutcomeFeedbackResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "feedback must be 'up' or 'down'."),
                statusCode: 400,
                message: "Invalid request");
        }

        var idempotencyKey = BuildIdempotencyKey(request, feedback);
        var existing = await _repository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var existingSummary = await BuildSummaryAsync(existing.TenantId, existing.TraceId, cancellationToken)
                .ConfigureAwait(false);
            _signalStore.Record(BuildSignal(existing, existingSummary));
            return ServiceResponse<OutcomeFeedbackResponseDto>.Success(ToResponse(existing, existingSummary));
        }

        var recordedUtc = DateTimeOffset.UtcNow;
        var record = new OutcomeFeedbackRecord
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            TraceId = request.TraceId.Trim(),
            TicketId = request.TicketId?.Trim(),
            ActionId = request.ActionId?.Trim(),
            ActorId = request.ActorId.Trim(),
            IdempotencyKey = idempotencyKey,
            FeedbackType = feedback,
            ConsentGranted = request.ConsentGranted,
            Notes = request.Note?.Trim(),
            ConfidenceBefore = request.ConfidenceBefore,
            ConfidenceAfter = request.ConfidenceAfter,
            CreatedUtc = recordedUtc
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await RecordLedgerEventAsync(record, cancellationToken).ConfigureAwait(false);

        var summary = await BuildSummaryAsync(record.TenantId, record.TraceId, cancellationToken).ConfigureAwait(false);

        _signalStore.Record(BuildSignal(record, summary));
        return ServiceResponse<OutcomeFeedbackResponseDto>.Success(ToResponse(record, summary));
    }

    public async Task<ServiceResponse<OutcomeFeedbackSummaryDto>> GetSummaryAsync(
        string tenantId,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(traceId))
        {
            return ServiceResponse<OutcomeFeedbackSummaryDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and trace_id are required."),
                statusCode: 400,
                message: "Invalid request");
        }

        var summary = await BuildSummaryAsync(tenantId.Trim(), traceId.Trim(), cancellationToken).ConfigureAwait(false);
        return ServiceResponse<OutcomeFeedbackSummaryDto>.Success(summary);
    }

    private async Task<OutcomeFeedbackSummaryDto> BuildSummaryAsync(
        string tenantId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var latest = await _repository.GetLatestAsync(tenantId, traceId, cancellationToken).ConfigureAwait(false);
        var upCount = await _repository.GetCountAsync(tenantId, traceId, "up", cancellationToken).ConfigureAwait(false);
        var downCount = await _repository.GetCountAsync(tenantId, traceId, "down", cancellationToken).ConfigureAwait(false);

        return new OutcomeFeedbackSummaryDto
        {
            TenantId = tenantId,
            TraceId = traceId,
            UpCount = upCount,
            DownCount = downCount,
            LastFeedback = latest?.FeedbackType,
            LastFeedbackUtc = latest?.CreatedUtc.UtcDateTime
        };
    }

    private static string? NormalizeFeedback(string value)
    {
        var trimmed = value?.Trim().ToLowerInvariant();
        return trimmed is "up" or "down" ? trimmed : null;
    }

    private static string BuildIdempotencyKey(OutcomeFeedbackRequestDto request, string feedback)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request.IdempotencyKey.Trim();
        }

        var parts = new[]
        {
            request.TenantId?.Trim(),
            request.TraceId?.Trim(),
            request.ActorId?.Trim(),
            request.ActionId?.Trim(),
            request.TicketId?.Trim(),
            feedback
        };

        return string.Join("|", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static OutcomeFeedbackResponseDto ToResponse(
        OutcomeFeedbackRecord record,
        OutcomeFeedbackSummaryDto summary)
    {
        return new OutcomeFeedbackResponseDto
        {
            TenantId = record.TenantId,
            TraceId = record.TraceId,
            TicketId = record.TicketId,
            ActionId = record.ActionId,
            ActorId = record.ActorId,
            IdempotencyKey = record.IdempotencyKey,
            Feedback = record.FeedbackType,
            ConsentGranted = record.ConsentGranted,
            Note = record.Notes,
            ConfidenceBefore = record.ConfidenceBefore,
            ConfidenceAfter = record.ConfidenceAfter,
            RecordedUtc = record.CreatedUtc.UtcDateTime,
            Summary = summary
        };
    }

    private static OutcomeFeedbackProfileSignal BuildSignal(
        OutcomeFeedbackRecord record,
        OutcomeFeedbackSummaryDto summary)
    {
        return new OutcomeFeedbackProfileSignal
        {
            TenantId = record.TenantId,
            TraceId = record.TraceId,
            TicketId = record.TicketId,
            ActionId = record.ActionId,
            ActorId = record.ActorId,
            Feedback = record.FeedbackType,
            UpCount = summary.UpCount,
            DownCount = summary.DownCount,
            LastFeedback = summary.LastFeedback,
            LastFeedbackUtc = summary.LastFeedbackUtc,
            RecordedUtc = record.CreatedUtc
        };
    }

    private async Task RecordLedgerEventAsync(OutcomeFeedbackRecord record, CancellationToken cancellationToken)
    {
        var payload = new
        {
            tenant_id = record.TenantId,
            trace_id = record.TraceId,
            ticket_id = record.TicketId,
            action_id = record.ActionId,
            actor_id = record.ActorId,
            idempotency_key = record.IdempotencyKey,
            feedback_type = record.FeedbackType,
            consent_granted = record.ConsentGranted,
            notes = record.Notes,
            confidence_before = record.ConfidenceBefore,
            confidence_after = record.ConfidenceAfter,
            recorded_utc = record.CreatedUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(record.TraceId, cancellationToken)
            .ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            record.TraceId,
            "OUTCOME_FEEDBACK_RECORDED",
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
