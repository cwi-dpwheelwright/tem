#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class ChatHistoryMemoryActivationService
{
    private static readonly Regex SecretPattern = new(
        @"\b(password|passwd|secret|token|api[_ -]?key|private[_ -]?key|credential|bearer)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TechnicalJsonPreferenceRegex = new(
        "^\"?[a-z0-9_.-]+/[a-z0-9_./-]+\"?\\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IGovernedMemoryRepository _memoryRepository;
    private readonly ImportedChatGraphProjectionBuilder _projectionBuilder;
    private readonly ActivatedMemoryOptions _options;

    public ChatHistoryMemoryActivationService(
        IGovernedMemoryRepository memoryRepository,
        ImportedChatGraphProjectionBuilder projectionBuilder,
        ActivatedMemoryOptions options)
    {
        _memoryRepository = memoryRepository;
        _projectionBuilder = projectionBuilder;
        _options = options;
    }

    public async Task ActivateAsync(
        IngestDocumentRequestDto request,
        SourceMemoryRecord source,
        string traceId,
        CancellationToken cancellationToken)
    {
        if (!ImportedChatGraphProjectionBuilder.IsMemoryActivationSource(request))
        {
            return;
        }

        var projection = _projectionBuilder.Build(request, traceId);
        var memories = projection.Nodes
            .Where(node => node.Labels.Contains("memory_fact", StringComparer.OrdinalIgnoreCase))
            .Select(node => TryBuildMemoryRecord(request, source, node, traceId))
            .Where(record => record is not null)
            .Select(record => record!)
            .ToArray();

        foreach (var memory in memories)
        {
            await _memoryRepository.UpsertMemoryAsync(memory, cancellationToken).ConfigureAwait(false);
        }

        await WriteLedgerEventAsync(
            eventType: "memory.capsules.created",
            request,
            source,
            traceId,
            memories.Length,
            status: "success",
            cancellationToken).ConfigureAwait(false);

        await WriteLedgerEventAsync(
            eventType: "memory.runtime.available",
            request,
            source,
            traceId,
            memories.Length,
            status: memories.Length == 0 ? "no_memory_facts" : "success",
            cancellationToken).ConfigureAwait(false);
    }

    private static GovernedMemoryRecord? TryBuildMemoryRecord(
        IngestDocumentRequestDto request,
        SourceMemoryRecord source,
        GraphIngestionNode node,
        string traceId)
    {
        var factText = GetString(node.Properties, "fact_text");
        if (string.IsNullOrWhiteSpace(factText)
            || SecretPattern.IsMatch(factText)
            || LooksLikeTechnicalExportArtifact(factText))
        {
            return null;
        }

        var domain = GetString(node.Properties, "memory_domain") ?? "general";
        var category = GetString(node.Properties, "memory_category") ?? "general_memory";
        var privacyClass = GetString(node.Properties, "privacy_class") ?? "private";
        var now = DateTimeOffset.UtcNow;
        var contentSha = HashText(source.TenantId, source.UserId, source.SourceId, category, factText);

        var memoryPrefix = ImportedChatGraphProjectionBuilder.IsChatHistoryImport(request)
            ? "mem-chat"
            : "mem-upload";
        return new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = $"{memoryPrefix}-{contentSha[..16]}",
            TenantId = source.TenantId,
            UserId = source.UserId,
            NodeId = source.NodeId,
            MemoryScope = "activated_user_memory",
            MemoryType = $"activated_user_memory {NormalizeDomain(domain)} {MapCategoryToMemoryType(category)} {category}",
            MemoryText = factText.Trim(),
            Confidence = 0.86f,
            SensitivityLevel = string.Equals(privacyClass, "work", StringComparison.OrdinalIgnoreCase) ? "private_work" : "private",
            RetentionPolicy = "user_owned",
            AccessScope = "user",
            LocalOnly = source.LocalOnly,
            SyncAllowed = source.SyncAllowed,
            ContentSha256 = contentSha,
            ProvenanceId = string.IsNullOrWhiteSpace(source.ProvenanceId) ? traceId : source.ProvenanceId,
            CreatedUtc = request.Document.CreatedUtc ?? now,
            UpdatedUtc = now
        };
    }

    private async Task WriteLedgerEventAsync(
        string eventType,
        IngestDocumentRequestDto request,
        SourceMemoryRecord source,
        string traceId,
        int memoryCount,
        string status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ActivationLedgerPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_options.ActivationLedgerPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var payload = new Dictionary<string, object?>
            {
                ["event_type"] = eventType,
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["tenant_id"] = source.TenantId,
                ["user_id"] = source.UserId,
                ["source_id"] = source.SourceId,
                ["source_sha256"] = source.ContentSha256,
                ["memory_count"] = memoryCount,
                ["status"] = status,
                ["provenance_id"] = traceId,
                ["source_type"] = ResolveLedgerSourceType(request, source)
            };

            await File.AppendAllTextAsync(
                _options.ActivationLedgerPath,
                JsonSerializer.Serialize(payload) + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Memory activation should not make source ingest fail; provenance is best-effort here.
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString()?.Trim(),
            _ => value.ToString()?.Trim()
        };
    }

    private static string ResolveLedgerSourceType(IngestDocumentRequestDto request, SourceMemoryRecord source)
    {
        return ImportedChatGraphProjectionBuilder.IsChatHistoryImport(request)
            ? "imported_chat_history"
            : string.IsNullOrWhiteSpace(source.SourceType)
                ? "uploaded_context"
                : source.SourceType;
    }

    private static string NormalizeDomain(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "work" => "work_career",
            "personal" => "home_personal",
            _ => "general"
        };
    }

    private static string MapCategoryToMemoryType(string category)
    {
        return category.Trim().ToLowerInvariant() switch
        {
            "current_work" => "project_memory",
            "open_loop" => "task_memory",
            "goal" => "goal_memory",
            "decision" => "decision_memory",
            "responsibility" => "relationship_memory",
            "home_life" => "preference_memory",
            "work_life" => "project_memory",
            _ => "event_memory"
        };
    }

    private static bool LooksLikeTechnicalExportArtifact(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        string[] technicalMarkers =
        {
            "oai/apps/",
            "hasseen",
            "nux",
            "tooltip",
            "localstorage",
            "sessionstorage",
            "feature flag",
            "feature_flag",
            "is_dismissed",
            "dismissed_at",
            "created_at",
            "updated_at"
        };

        if (technicalMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            return true;
        }

        return TechnicalJsonPreferenceRegex.IsMatch(normalized);
    }

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
