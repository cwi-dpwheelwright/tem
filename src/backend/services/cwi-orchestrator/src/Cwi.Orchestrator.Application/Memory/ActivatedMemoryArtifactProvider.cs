#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class ActivatedMemoryArtifactProvider
{
    private const int DefaultLimit = 12;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] SecretMarkers =
    {
        "api key",
        "password",
        "private key",
        "secret token",
        "access token",
        "bearer ",
        "-----begin"
    };

    private readonly ActivatedMemoryOptions _options;

    public ActivatedMemoryArtifactProvider(ActivatedMemoryOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<GovernedMemoryResult>> RetrieveAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (!IsMemoryRecallContext(context) || string.IsNullOrWhiteSpace(_options.ActivatedDirectory))
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var directory = _options.ActivatedDirectory;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var capsules = new List<ActivatedMemoryCapsule>();
        await LoadMemoryCapsulesAsync(directory, capsules, cancellationToken).ConfigureAwait(false);
        if (await IsStructuredArtifactScopeAllowedAsync(directory, context, cancellationToken).ConfigureAwait(false))
        {
            await LoadStructuredArtifactsAsync(directory, capsules, context, cancellationToken).ConfigureAwait(false);
        }

        if (capsules.Count == 0)
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        return capsules
            .Where(capsule => IsAllowedForUser(capsule, context))
            .Where(capsule => !ContainsSecret(capsule.Content))
            .Select(capsule => ToResult(capsule, context))
            .Where(result => result is not null)
            .Select(result => result!)
            .GroupBy(result => result.MemoryId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.RelevanceScore).First())
            .OrderByDescending(result => result.RelevanceScore)
            .ThenByDescending(result => result.Confidence)
            .Take(ResolveLimit(context))
            .ToArray();
    }

    public async Task WriteRetrievalLedgerAsync(
        AskWorkflowContext context,
        IReadOnlyList<GovernedMemoryResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0 || string.IsNullOrWhiteSpace(_options.ActivationLedgerPath))
        {
            return;
        }

        try
        {
            var ledgerDirectory = Path.GetDirectoryName(_options.ActivationLedgerPath);
            if (!string.IsNullOrWhiteSpace(ledgerDirectory))
            {
                Directory.CreateDirectory(ledgerDirectory);
            }

            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["event_type"] = "memory.retrieved",
                ["timestamp_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["tenant_id"] = context.TenantId,
                ["user_id"] = context.UserId,
                ["request_id"] = context.RequestId,
                ["trace_id"] = context.TraceId,
                ["query_classification"] = context.QueryClassification,
                ["intent_type"] = context.IntentType,
                ["memory_count"] = results.Count,
                ["memory_ids"] = results.Select(result => result.MemoryId).ToArray(),
                ["status"] = "success"
            }, JsonOptions);

            await File.AppendAllTextAsync(_options.ActivationLedgerPath, payload + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (!context.ResponseWarnings.Contains("memory_activation_ledger_unavailable", StringComparer.OrdinalIgnoreCase))
            {
                context.ResponseWarnings.Add("memory_activation_ledger_unavailable");
            }
        }
    }

    private static bool IsMemoryRecallContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || AskUploadedContextLookupPolicy.IsUploadedContextLookup(context)
               || (context.RetrievalDomain?.Contains("memory_graph", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static async Task LoadMemoryCapsulesAsync(
        string directory,
        ICollection<ActivatedMemoryCapsule> capsules,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "memory_capsules.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var capsule = JsonSerializer.Deserialize<ActivatedMemoryCapsule>(line, JsonOptions);
                if (capsule is not null && !string.IsNullOrWhiteSpace(capsule.Content))
                {
                    capsules.Add(capsule.Normalize());
                }
            }
            catch (JsonException)
            {
                // Skip malformed import records; one bad capsule should not block memory retrieval.
            }
        }
    }

    private static async Task LoadStructuredArtifactsAsync(
        string directory,
        ICollection<ActivatedMemoryCapsule> capsules,
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        string[] artifactNames =
        {
            "user_profile.json",
            "relationship_graph.json",
            "project_graph.json",
            "preferences.json",
            "open_loops.json",
            "decisions.json"
        };

        foreach (var name in artifactNames)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                foreach (var text in FlattenText(document.RootElement).Where(text => !string.IsNullOrWhiteSpace(text)).Take(50))
                {
                    capsules.Add(new ActivatedMemoryCapsule
                    {
                        MemoryId = $"artifact_{StableHash(name, text)}",
                        TenantId = context.TenantId,
                        UserId = context.UserId,
                        MemoryType = ResolveArtifactMemoryType(name),
                        Domain = ResolveArtifactDomain(name),
                        Content = text,
                        SourceType = "activated_memory_artifact",
                        SourceId = name,
                        Confidence = 0.78f,
                        SensitivityLevel = "private",
                        ProvenanceId = name
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Artifact sidecars are optional; capsules remain the canonical activation record.
            }
        }
    }

    private GovernedMemoryResult? ToResult(ActivatedMemoryCapsule capsule, AskWorkflowContext context)
    {
        var text = Compact(capsule.Content, 900);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var relevance = Score(capsule, text, context);
        if (relevance < 0.70f)
        {
            return null;
        }

        var confidence = Math.Clamp(capsule.Confidence ?? 0.82f, 0.0f, 0.99f);
        var domain = string.IsNullOrWhiteSpace(capsule.Domain) ? "general" : capsule.Domain.Trim();
        var type = string.IsNullOrWhiteSpace(capsule.MemoryType) ? "memory" : capsule.MemoryType.Trim();
        var category = string.IsNullOrWhiteSpace(capsule.MemoryCategory) ? string.Empty : capsule.MemoryCategory.Trim();
        return new GovernedMemoryResult
        {
            MemoryId = string.IsNullOrWhiteSpace(capsule.MemoryId) ? $"mem_{StableHash(text)}" : capsule.MemoryId.Trim(),
            MemoryScope = "activated_user_memory",
            SourceType = $"activated_user_memory {domain} {type} {category}".Trim(),
            Text = text,
            TenantId = string.IsNullOrWhiteSpace(capsule.TenantId) ? context.TenantId : capsule.TenantId.Trim(),
            UserId = string.IsNullOrWhiteSpace(capsule.UserId) ? context.UserId : capsule.UserId.Trim(),
            RelevanceScore = relevance,
            Confidence = confidence,
            ContentSha256 = string.IsNullOrWhiteSpace(capsule.SourceSha256) ? HashText(text) : capsule.SourceSha256.Trim(),
            ProvenanceId = string.IsNullOrWhiteSpace(capsule.ProvenanceId)
                ? (string.IsNullOrWhiteSpace(capsule.SourceId) ? null : capsule.SourceId.Trim())
                : capsule.ProvenanceId.Trim()
        };
    }

    private static bool IsAllowedForUser(ActivatedMemoryCapsule capsule, AskWorkflowContext context)
    {
        var tenantMatches = !string.IsNullOrWhiteSpace(capsule.TenantId)
            && string.Equals(capsule.TenantId.Trim(), context.TenantId, StringComparison.OrdinalIgnoreCase);
        var userMatches = !string.IsNullOrWhiteSpace(capsule.UserId)
            && string.Equals(capsule.UserId.Trim(), context.UserId, StringComparison.OrdinalIgnoreCase);
        return tenantMatches && userMatches;
    }

    private static async Task<bool> IsStructuredArtifactScopeAllowedAsync(
        string directory,
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, "source_manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var tenantId = TryGetString(document.RootElement, "tenant_id");
            var userId = TryGetString(document.RootElement, "user_id");
            return !string.IsNullOrWhiteSpace(tenantId)
                   && !string.IsNullOrWhiteSpace(userId)
                   && string.Equals(tenantId.Trim(), context.TenantId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(userId.Trim(), context.UserId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static float Score(ActivatedMemoryCapsule capsule, string text, AskWorkflowContext context)
    {
        var score = 0.74f;
        var domain = capsule.Domain ?? string.Empty;
        var type = capsule.MemoryType ?? string.Empty;
        var category = capsule.MemoryCategory ?? string.Empty;
        var haystack = $"{domain} {type} {category} {text}";
        var query = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        var retrievalDomain = context.RetrievalDomain ?? string.Empty;

        if (query.Contains("what do you know about me", StringComparison.Ordinal)
            || query.Contains("what do you understand about me", StringComparison.Ordinal)
            || query.Contains("what have you learned about me", StringComparison.Ordinal)
            || query.Contains("what have you learnt about me", StringComparison.Ordinal)
            || string.Equals(retrievalDomain, "user_memory_graph", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.12f;
        }

        if (retrievalDomain.Contains("work", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, new[] { "work", "project", "business", "career", "operations", "technology", "mcp", "cwi" }))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("personal", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, new[] { "home", "personal", "family", "health", "wellness", "finance", "pets" }))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("open_loop", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, new[] { "open_loop", "task", "todo", "to do", "need to", "pending", "follow up", "blocked" }))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("relationship", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, new[] { "relationship", "person", "people", "collaborator", "demitrius", "chad", "jasmin", "christian" }))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("source", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, new[] { "source", "import", "provenance", "chat_history", "memory_capsule" }))
        {
            score += 0.12f;
        }

        foreach (var term in context.RetrievalDomainTerms)
        {
            if (!string.IsNullOrWhiteSpace(term) && haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.05f;
            }
        }

        return Math.Min(0.99f, score);
    }

    private static int ResolveLimit(AskWorkflowContext context)
    {
        if (context.TopK > 0)
        {
            return Math.Clamp(context.TopK * 2, 6, 16);
        }

        return DefaultLimit;
    }

    private static IEnumerable<string> FlattenText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 12)
                {
                    yield return text.Trim();
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in FlattenText(item))
                    {
                        yield return value;
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in FlattenText(property.Value))
                    {
                        yield return value;
                    }
                }
                break;
        }
    }

    private static string ResolveArtifactMemoryType(string fileName)
    {
        return fileName switch
        {
            "relationship_graph.json" => "relationship_memory",
            "project_graph.json" => "project_memory",
            "preferences.json" => "preference_memory",
            "open_loops.json" => "task_memory",
            "decisions.json" => "decision_memory",
            _ => "profile_memory"
        };
    }

    private static string ResolveArtifactDomain(string fileName)
    {
        return fileName switch
        {
            "relationship_graph.json" => "relationships",
            "project_graph.json" => "work_career",
            "open_loops.json" => "open_loops",
            "decisions.json" => "work_career",
            _ => "user_profile"
        };
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return SecretMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static string Compact(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = string.Join(
            " ",
            text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static string StableHash(params string?[] parts)
    {
        return HashText(parts)[..16];
    }

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class ActivatedMemoryCapsule
    {
        [JsonPropertyName("memory_id")]
        public string? MemoryId { get; init; }

        [JsonPropertyName("tenant_id")]
        public string? TenantId { get; init; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; init; }

        [JsonPropertyName("memory_type")]
        public string? MemoryType { get; init; }

        [JsonPropertyName("memory_category")]
        public string? MemoryCategory { get; init; }

        [JsonPropertyName("domain")]
        public string? Domain { get; init; }

        [JsonPropertyName("memory_domain")]
        public string? MemoryDomain { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("memory_text")]
        public string? MemoryText { get; init; }

        [JsonPropertyName("fact_text")]
        public string? FactText { get; init; }

        [JsonPropertyName("source_type")]
        public string? SourceType { get; init; }

        [JsonPropertyName("source_id")]
        public string? SourceId { get; init; }

        [JsonPropertyName("source_sha256")]
        public string? SourceSha256 { get; init; }

        [JsonPropertyName("confidence")]
        public float? Confidence { get; init; }

        [JsonPropertyName("sensitivity_level")]
        public string? SensitivityLevel { get; init; }

        [JsonPropertyName("provenance_id")]
        public string? ProvenanceId { get; init; }

        public ActivatedMemoryCapsule Normalize()
        {
            return new ActivatedMemoryCapsule
            {
                MemoryId = MemoryId,
                TenantId = TenantId,
                UserId = UserId,
                MemoryType = MemoryType,
                MemoryCategory = MemoryCategory,
                Domain = string.IsNullOrWhiteSpace(Domain) ? MemoryDomain : Domain,
                MemoryDomain = MemoryDomain,
                Content = FirstNonEmpty(Content, MemoryText, FactText),
                MemoryText = MemoryText,
                FactText = FactText,
                SourceType = SourceType,
                SourceId = SourceId,
                SourceSha256 = SourceSha256,
                Confidence = Confidence,
                SensitivityLevel = SensitivityLevel,
                ProvenanceId = ProvenanceId
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
        }
    }
}
#nullable disable
