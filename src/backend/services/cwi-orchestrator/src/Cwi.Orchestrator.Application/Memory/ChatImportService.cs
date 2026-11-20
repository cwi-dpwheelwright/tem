#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class ChatImportService
{
    private const int DefaultListLimit = 50;
    private const int DefaultChunkLimit = 500;
    private readonly ISourceMemoryRepository _sourceMemoryRepository;

    public ChatImportService(ISourceMemoryRepository sourceMemoryRepository)
    {
        _sourceMemoryRepository = sourceMemoryRepository;
    }

    public async Task<ServiceResponse<IReadOnlyList<ChatImportSummaryDto>>> ListAsync(
        string tenantId,
        string? userId,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<IReadOnlyList<ChatImportSummaryDto>>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        var sources = await _sourceMemoryRepository
            .ListSourcesAsync(tenantId.Trim(), NormalizeUserId(userId), "chat_history_import", limit.GetValueOrDefault(DefaultListLimit), cancellationToken)
            .ConfigureAwait(false);

        return ServiceResponse<IReadOnlyList<ChatImportSummaryDto>>.Success(sources.Select(ToSummaryDto).ToArray());
    }

    public async Task<ServiceResponse<ChatImportDetailDto>> GetAsync(
        string tenantId,
        string sourceId,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sourceId))
        {
            return ServiceResponse<ChatImportDetailDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id and import id are required."),
                statusCode: 400,
                message: "tenant_id and import id are required");
        }

        var source = await _sourceMemoryRepository
            .GetSourceAsync(tenantId.Trim(), sourceId.Trim(), NormalizeUserId(userId), cancellationToken)
            .ConfigureAwait(false);
        if (source is null || !string.Equals(source.SourceType, "chat_history_import", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<ChatImportDetailDto>.Fail(
                new DomainError("not_found", "Chat import not found."),
                statusCode: 404,
                message: "Chat import not found");
        }

        var chunks = await _sourceMemoryRepository
            .ListChunksForSourceAsync(tenantId.Trim(), sourceId.Trim(), NormalizeUserId(userId), DefaultChunkLimit, cancellationToken)
            .ConfigureAwait(false);

        var summary = ToSummaryDto(source);
        return ServiceResponse<ChatImportDetailDto>.Success(new ChatImportDetailDto
        {
            ImportId = summary.ImportId,
            TenantId = summary.TenantId,
            UserId = summary.UserId,
            Title = summary.Title,
            SourceType = summary.SourceType,
            StorageLocation = summary.StorageLocation,
            ProvenanceId = summary.ProvenanceId,
            CreatedUtc = summary.CreatedUtc,
            Chunks = chunks.Select(ToChunkDto).ToArray()
        });
    }

    private static ChatImportSummaryDto ToSummaryDto(SourceMemoryRecord source)
    {
        return new ChatImportSummaryDto
        {
            ImportId = source.SourceId,
            TenantId = source.TenantId,
            UserId = source.UserId,
            Title = source.Filename,
            SourceType = source.SourceType,
            StorageLocation = source.StorageLocation,
            ProvenanceId = source.ProvenanceId,
            CreatedUtc = source.CreatedUtc
        };
    }

    private static ChatImportChunkDto ToChunkDto(SourceChunkRecord chunk)
    {
        return new ChatImportChunkDto
        {
            ChunkId = chunk.ChunkId,
            ChunkIndex = chunk.ChunkIndex,
            Text = chunk.ChunkText,
            ContentSha256 = chunk.ContentSha256,
            CreatedUtc = chunk.CreatedUtc
        };
    }

    private static string? NormalizeUserId(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
    }
}
#nullable disable
