using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.History;

public interface IAskHistoryService
{
    Task<ServiceResponse<AskHistoryRecordDto>> RecordAsync(
        string tenantId,
        string userId,
        AskHistoryRecordRequestDto request,
        CancellationToken cancellationToken);

    Task<ServiceResponse<AskHistoryListResponseDto>> ListAsync(
        string tenantId,
        string userId,
        int? limit,
        int? retentionDays,
        CancellationToken cancellationToken);

    Task<ServiceResponse<AskHistoryClearResponseDto>> ClearAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken);
}

public sealed class AskHistoryService : IAskHistoryService
{
    private const int DefaultRetentionDays = 30;
    private const int DefaultMaxEntries = 25;
    private const int MaxEntriesCap = 200;

    private readonly IAskHistoryRepository _repository;

    public AskHistoryService(IAskHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<AskHistoryRecordDto>> RecordAsync(
        string tenantId,
        string userId,
        AskHistoryRecordRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResponse<AskHistoryRecordDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and user_id are required."),
                statusCode: 400,
                message: "Invalid request");
        }

        var responseStatus = NormalizeResponseStatus(request.ResponseStatus, request.Answer);
        if (string.IsNullOrWhiteSpace(request.Question)
            || (string.IsNullOrWhiteSpace(request.Answer) && responseStatus is not "processing"))
        {
            return ServiceResponse<AskHistoryRecordDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "question is required; answer is required unless response_status is processing."),
                statusCode: 400,
                message: "Invalid request");
        }

        var normalizedLimit = NormalizeLimit(request.MaxEntries);
        var normalizedRetentionDays = NormalizeRetentionDays(request.RetentionDays);
        var recordedUtc = DateTimeOffset.UtcNow;

        var recordId = Guid.NewGuid();
        var question = request.Question.Trim();
        var answer = request.Answer?.Trim() ?? string.Empty;
        var traceId = request.TraceId?.Trim();
        var record = new AskHistoryRecord
        {
            Id = recordId,
            RequestId = request.RequestId?.Trim(),
            TenantId = tenantId.Trim(),
            UserId = userId.Trim(),
            ConversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? "default" : request.ConversationId.Trim(),
            TurnId = recordId.ToString("N"),
            Question = question,
            Answer = answer,
            TraceId = traceId,
            ProvenanceId = traceId,
            PackName = request.PackName?.Trim(),
            PackHash = request.PackHash?.Trim(),
            ContentSha256 = HashText(question, answer),
            SummaryStatus = responseStatus,
            TraceJson = SerializeTrace(request.Trace),
            AttachmentsJson = SerializeAttachments(request.Attachments),
            CreatedUtc = recordedUtc
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await EnforceRetentionAsync(
            record.TenantId,
            record.UserId,
            normalizedLimit,
            normalizedRetentionDays,
            recordedUtc,
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<AskHistoryRecordDto>.Success(ToDto(record));
    }

    public async Task<ServiceResponse<AskHistoryListResponseDto>> ListAsync(
        string tenantId,
        string userId,
        int? limit,
        int? retentionDays,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResponse<AskHistoryListResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and user_id are required."),
                statusCode: 400,
                message: "Invalid request");
        }

        var normalizedLimit = NormalizeLimit(limit);
        var normalizedRetentionDays = NormalizeRetentionDays(retentionDays);
        var now = DateTimeOffset.UtcNow;

        await EnforceRetentionAsync(
            tenantId.Trim(),
            userId.Trim(),
            normalizedLimit,
            normalizedRetentionDays,
            now,
            cancellationToken).ConfigureAwait(false);

        var cutoff = BuildCutoff(normalizedRetentionDays, now);
        var records = await _repository
            .ListAsync(tenantId.Trim(), userId.Trim(), normalizedLimit, cutoff, cancellationToken)
            .ConfigureAwait(false);

        var response = new AskHistoryListResponseDto
        {
            Items = records.Select(ToDto).ToArray()
        };

        return ServiceResponse<AskHistoryListResponseDto>.Success(response);
    }

    public async Task<ServiceResponse<AskHistoryClearResponseDto>> ClearAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResponse<AskHistoryClearResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and user_id are required."),
                statusCode: 400,
                message: "Invalid request");
        }

        var deleted = await _repository.DeleteAllAsync(tenantId.Trim(), userId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        return ServiceResponse<AskHistoryClearResponseDto>.Success(new AskHistoryClearResponseDto
        {
            Deleted = deleted
        });
    }

    private static int NormalizeLimit(int? limit)
    {
        if (!limit.HasValue)
        {
            return DefaultMaxEntries;
        }

        if (limit.Value <= 0)
        {
            return DefaultMaxEntries;
        }

        return Math.Min(limit.Value, MaxEntriesCap);
    }

    private static int NormalizeRetentionDays(int? retentionDays)
    {
        if (!retentionDays.HasValue)
        {
            return DefaultRetentionDays;
        }

        return retentionDays.Value < 0 ? DefaultRetentionDays : retentionDays.Value;
    }

    private static string NormalizeResponseStatus(string? responseStatus, string? answer)
    {
        var normalized = responseStatus?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "processing" => "processing",
            "completed" => "completed",
            "failed" => "failed",
            "pending" when string.IsNullOrWhiteSpace(answer) => "processing",
            "pending" => "completed",
            _ => string.IsNullOrWhiteSpace(answer) ? "processing" : "completed"
        };
    }

    private static string ResolveResponseStatus(AskHistoryRecord record)
    {
        var status = record.SummaryStatus?.Trim().ToLowerInvariant();
        return status switch
        {
            "processing" => "processing",
            "failed" => "failed",
            "completed" => "completed",
            "pending" when string.IsNullOrWhiteSpace(record.Answer) => "processing",
            _ => string.IsNullOrWhiteSpace(record.Answer) ? "processing" : "completed"
        };
    }

    private static DateTimeOffset? BuildCutoff(int retentionDays, DateTimeOffset now)
    {
        if (retentionDays <= 0)
        {
            return null;
        }

        return now.AddDays(-retentionDays);
    }

    private async Task EnforceRetentionAsync(
        string tenantId,
        string userId,
        int maxEntries,
        int retentionDays,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cutoff = BuildCutoff(retentionDays, now);
        if (cutoff is not null)
        {
            await _repository.DeleteOlderThanAsync(tenantId, userId, cutoff.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        if (maxEntries > 0)
        {
            await _repository.TrimToLatestAsync(tenantId, userId, maxEntries, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string? SerializeTrace(JsonElement? trace)
    {
        if (trace is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(trace.Value);
    }

    private static string? SerializeAttachments(IReadOnlyList<AskHistoryAttachmentDto>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return null;
        }

        var sanitized = attachments
            .Where(static attachment => !string.IsNullOrWhiteSpace(attachment.Name))
            .Select(static attachment => new AskHistoryAttachmentDto
            {
                Name = attachment.Name.Trim(),
                MimeType = attachment.MimeType?.Trim() ?? string.Empty,
                Size = Math.Max(0, attachment.Size),
                Kind = attachment.Kind?.Trim() ?? string.Empty,
                Excerpt = string.IsNullOrWhiteSpace(attachment.Excerpt) ? null : attachment.Excerpt.Trim(),
                Truncated = attachment.Truncated,
                PreviewDataUrl = string.IsNullOrWhiteSpace(attachment.PreviewDataUrl) ? null : attachment.PreviewDataUrl.Trim()
            })
            .ToArray();

        return sanitized.Length == 0 ? null : JsonSerializer.Serialize(sanitized);
    }

    private static JsonElement? ParseTrace(string? traceJson)
    {
        if (string.IsNullOrWhiteSpace(traceJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(traceJson);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AskHistoryAttachmentDto> ParseAttachments(string? attachmentsJson)
    {
        if (string.IsNullOrWhiteSpace(attachmentsJson))
        {
            return Array.Empty<AskHistoryAttachmentDto>();
        }

        try
        {
            var items = JsonSerializer.Deserialize<AskHistoryAttachmentDto[]>(attachmentsJson);
            return items ?? Array.Empty<AskHistoryAttachmentDto>();
        }
        catch
        {
            return Array.Empty<AskHistoryAttachmentDto>();
        }
    }

    private static AskHistoryRecordDto ToDto(AskHistoryRecord record)
    {
        return new AskHistoryRecordDto
        {
            Id = record.Id.ToString(),
            RequestId = record.RequestId,
            ConversationId = record.ConversationId,
            TurnId = record.TurnId,
            Question = record.Question,
            Answer = record.Answer,
            ResponseStatus = ResolveResponseStatus(record),
            CreatedAt = record.CreatedUtc,
            TraceId = record.TraceId,
            ProvenanceId = record.ProvenanceId,
            PackName = record.PackName,
            PackHash = record.PackHash,
            ContentSha256 = record.ContentSha256,
            SummaryStatus = record.SummaryStatus,
            Trace = ParseTrace(record.TraceJson),
            Attachments = ParseAttachments(record.AttachmentsJson)
        };
    }

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
