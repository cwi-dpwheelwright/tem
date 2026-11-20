using System.Security.Cryptography;
using System.Text;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Etl;

public sealed class IngestDocumentService : DomainServiceBase
{
    private const int SourceChunkMaxChars = 4000;

    private readonly IngestDocumentSchemaValidator _validator;
    private readonly TraceLookupStore _traceStore;
    private readonly DocumentStore _documentStore;
    private readonly ISourceMemoryRepository _sourceMemoryRepository;
    private readonly IImportedChatGraphIngestionService? _graphIngestionService;
    private readonly ChatHistoryMemoryActivationService? _chatHistoryMemoryActivationService;

    public IngestDocumentService(
        IngestDocumentSchemaValidator validator,
        TraceLookupStore traceStore,
        DocumentStore documentStore,
        ISourceMemoryRepository sourceMemoryRepository,
        IImportedChatGraphIngestionService? graphIngestionService = null,
        ChatHistoryMemoryActivationService? chatHistoryMemoryActivationService = null)
    {
        _validator = validator;
        _traceStore = traceStore;
        _documentStore = documentStore;
        _sourceMemoryRepository = sourceMemoryRepository;
        _graphIngestionService = graphIngestionService;
        _chatHistoryMemoryActivationService = chatHistoryMemoryActivationService;
    }

    public Task<ServiceResponse<IngestDocumentResponseDto>> HandleAsync(
        IngestDocumentRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() =>
        {
            var validation = _validator.Validate(request);
            if (!validation.IsSuccess)
            {
                if (!IsManuallyValid(request))
                {
                    return Task.FromResult(ServiceResponse<IngestDocumentResponseDto>.Fail(
                        validation.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Ingest document invalid."),
                        statusCode: validation.StatusCode,
                        message: validation.Message));
                }
            }

            var traceId = Guid.NewGuid().ToString("N");
            _traceStore.Record(traceId, request.Document.DocId);
            _documentStore.RecordIngest(request);
            return PersistSourceAsync(request, traceId, cancellationToken);
        });
    }

    private async Task<ServiceResponse<IngestDocumentResponseDto>> PersistSourceAsync(
        IngestDocumentRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var payload = request.Document;
        var content = NormalizeContent(payload.Content, payload.ContentEncoding);
        var sourceId = payload.DocId.Trim();
        var version = ResolveVersion(request);
        var now = DateTimeOffset.UtcNow;
        var source = new SourceMemoryRecord
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            TenantId = request.TenantId.Trim(),
            UserId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim(),
            ConversationId = ResolveStringMetadata(payload.Metadata, "conversation_id"),
            NodeId = ResolveStringMetadata(payload.Metadata, "node_id"),
            Filename = string.IsNullOrWhiteSpace(payload.Title) ? sourceId : payload.Title.Trim(),
            SourceType = ResolveSourceType(request),
            StorageLocation = ResolveStringMetadata(payload.Metadata, "storage_location") ?? "orchestrator_source_memory",
            ExtractionMethod = ResolveStringMetadata(payload.Metadata, "extraction_method") ?? "server_side_text_ingest",
            LocalOnly = ResolveBoolMetadata(payload.Metadata, "local_only"),
            SyncAllowed = !ResolveBoolMetadata(payload.Metadata, "local_only"),
            ContentSha256 = HashText(content),
            ProvenanceId = traceId,
            CreatedUtc = payload.CreatedUtc ?? now
        };
        var chunks = BuildSourceChunks(request, source, version, content);
        await _sourceMemoryRepository.UpsertSourceAsync(source, chunks, cancellationToken).ConfigureAwait(false);
        await TryIngestGraphAsync(request, traceId, cancellationToken).ConfigureAwait(false);
        await TryActivateImportedChatMemoryAsync(request, source, traceId, cancellationToken).ConfigureAwait(false);

        var response = new IngestDocumentResponseDto
        {
            Accepted = true,
            IngestId = Guid.NewGuid().ToString("N"),
            TraceId = traceId,
            DocId = request.Document.DocId,
            Message = "accepted"
        };

        return ServiceResponse<IngestDocumentResponseDto>.Success(response);
    }

    private async Task TryIngestGraphAsync(
        IngestDocumentRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (_graphIngestionService is null)
        {
            return;
        }

        try
        {
            await _graphIngestionService.IngestAsync(request, traceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Graph ingestion is additive memory enrichment. Source ingest remains authoritative.
        }
    }

    private async Task TryActivateImportedChatMemoryAsync(
        IngestDocumentRequestDto request,
        SourceMemoryRecord source,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (_chatHistoryMemoryActivationService is null)
        {
            return;
        }

        try
        {
            await _chatHistoryMemoryActivationService
                .ActivateAsync(request, source, traceId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Runtime memory activation is additive. Source ingest and graph ingest remain authoritative.
        }
    }

    private static IReadOnlyList<SourceChunkRecord> BuildSourceChunks(
        IngestDocumentRequestDto request,
        SourceMemoryRecord source,
        string version,
        string content)
    {
        var chunks = new List<SourceChunkRecord>();
        var normalized = content.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return chunks;
        }

        var index = 0;
        for (var offset = 0; offset < normalized.Length; offset += SourceChunkMaxChars)
        {
            var text = normalized.Substring(offset, Math.Min(SourceChunkMaxChars, normalized.Length - offset)).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            chunks.Add(new SourceChunkRecord
            {
                Id = Guid.NewGuid(),
                SourceId = source.SourceId,
                ChunkId = chunks.Count == 0 && normalized.Length <= SourceChunkMaxChars
                    ? version
                    : $"{version}-chunk-{index:D4}",
                TenantId = source.TenantId,
                UserId = source.UserId,
                ConversationId = source.ConversationId,
                NodeId = source.NodeId,
                ChunkText = text,
                ChunkIndex = index,
                HeadingContext = ResolveStringMetadata(request.Document.Metadata, "heading_context"),
                LocalOnly = source.LocalOnly,
                ContentSha256 = HashText(source.SourceId, version, index.ToString(), text),
                EmbeddingId = ResolveStringMetadata(request.Document.Metadata, "embedding_id"),
                CreatedUtc = source.CreatedUtc
            });
            index++;
        }

        return chunks;
    }

    private static bool IsManuallyValid(IngestDocumentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.TenantId))
        {
            return false;
        }

        var document = request.Document;
        if (document is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.DocId) ||
            string.IsNullOrWhiteSpace(document.Title) ||
            string.IsNullOrWhiteSpace(document.MimeType) ||
            string.IsNullOrWhiteSpace(document.Content) ||
            string.IsNullOrWhiteSpace(document.ContentEncoding) ||
            string.IsNullOrWhiteSpace(document.Source))
        {
            return false;
        }

        return string.Equals(document.ContentEncoding, "plain", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(document.ContentEncoding, "base64", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeContent(string content, string? encoding)
    {
        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(content));
        }

        return content;
    }

    private static string ResolveVersion(IngestDocumentRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            return request.Version.Trim();
        }

        return ResolveStringMetadata(request.Document.Metadata, "version") ?? "v1";
    }

    private static string ResolveSourceType(IngestDocumentRequestDto request)
    {
        var explicitSourceType = ResolveStringMetadata(request.Document.Metadata, "source_type");
        if (!string.IsNullOrWhiteSpace(explicitSourceType))
        {
            return explicitSourceType;
        }

        return ImportedChatGraphProjectionBuilder.IsChatHistoryImport(request)
            ? "chat_history_import"
            : "uploaded_source";
    }

    private static string? ResolveStringMetadata(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
    }

    private static bool ResolveBoolMetadata(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && value switch
        {
            bool flag => flag,
            string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1",
            _ => false
        };
    }

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
