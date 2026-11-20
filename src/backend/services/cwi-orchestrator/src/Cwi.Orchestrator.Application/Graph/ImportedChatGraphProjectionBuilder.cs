#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class ImportedChatGraphProjectionBuilder
{
    private const int MaxFactsPerDocument = 40;
    private static readonly Regex SpeakerPrefixRegex = new(@"^\s*(?:user|assistant|system|tool)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SentenceSplitRegex = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    private static readonly string[] ChatImportTags =
    {
        "chat-history-import",
        "chat_history",
        "structured_transcript"
    };

    private static readonly string[] UserUploadTags =
    {
        "context-upload",
        "uploaded_context",
        "source-test",
        "gpt-web-ui"
    };

    private static readonly string[] PersonalTerms =
    {
        "home", "personal", "family", "kid", "kids", "child", "children", "spouse", "wife", "husband",
        "partner", "parent", "doctor", "health", "routine", "chores", "house", "school", "appointment",
        "pet", "dog", "cat"
    };

    private static readonly string[] WorkTerms =
    {
        "work", "working", "project", "client", "customer", "deploy", "deployment", "repo", "backend",
        "frontend", "api", "system", "runtime", "orchestrator", "graph", "memory", "meeting", "demo",
        "investor", "product", "business", "team", "dmitrius", "demitrius", "cwi"
    };

    private static readonly string[] OpenLoopTerms =
    {
        "open loop", "need to", "needs to", "todo", "to do", "follow up", "next action", "next step",
        "pending", "blocked", "still need", "must", "this week", "tomorrow", "open-", "open_", "verify"
    };

    private static readonly string[] GoalTerms =
    {
        "goal", "goals", "objective", "objectives", "trying to", "want to", "planning to", "plan to",
        "aim", "north star"
    };

    private static readonly string[] DecisionTerms =
    {
        "decided", "decision", "agreed", "choose", "chose", "settled on", "approved", "rejected"
    };

    private static readonly string[] ResponsibilityTerms =
    {
        "responsible", "responsibility", "own", "owns", "owner", "manage", "manages", "takes care"
    };

    private static readonly string[] ExcludedActiveMemoryTerms =
    {
        "retracted",
        "retract-",
        "deleted_memory",
        "historical_memory",
        "historical context",
        "older context",
        "remove this memory",
        "not the current",
        "must not be used",
        "should not be used",
        "false marker",
        "expired_memory",
        "no longer valid"
    };

    private static readonly string[] TechnicalExportTerms =
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

    private static readonly Regex TechnicalJsonPreferenceRegex = new(
        "^\"?[a-z0-9_.-]+/[a-z0-9_./-]+\"?\\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsChatHistoryImport(IngestDocumentRequestDto request)
    {
        if (request.Tags.Any(tag => ChatImportTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (TryGetMetadataString(request.Document.Metadata, "import_kind", out var importKind)
            && string.Equals(importKind, "chat_history", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryGetMetadataString(request.Document.Metadata, "normalization_mode", out var normalizationMode)
            && string.Equals(normalizationMode, "structured_transcript", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Document.Metadata.ContainsKey("message_count");
    }

    public static bool IsMemoryActivationSource(IngestDocumentRequestDto request)
    {
        if (IsChatHistoryImport(request))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return false;
        }

        if (request.Tags.Any(tag => UserUploadTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (TryGetMetadataString(request.Document.Metadata, "uploaded_via", out _))
        {
            return true;
        }

        if (TryGetMetadataString(request.Document.Metadata, "import_kind", out var importKind)
            && !string.Equals(importKind, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryGetMetadataString(request.Document.Metadata, "source_type", out var sourceType)
            && sourceType.Contains("context", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(request.Document.Source, "cwi-gpt-web-ui", StringComparison.OrdinalIgnoreCase);
    }

    public GraphIngestionProjection Build(IngestDocumentRequestDto request, string traceId)
    {
        if (!IsMemoryActivationSource(request))
        {
            return new GraphIngestionProjection(Array.Empty<GraphIngestionNode>(), Array.Empty<GraphIngestionEdge>());
        }

        var sourceProfile = ResolveSourceProfile(request);
        var tenantId = request.TenantId.Trim();
        var userId = string.IsNullOrWhiteSpace(request.UserId) ? null : request.UserId.Trim();
        var sourceId = request.Document.DocId.Trim();
        var observedUtc = (request.Document.CreatedUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O");
        var uploadBatchId = TryGetMetadataString(request.Document.Metadata, "upload_batch_id", out var batchId)
            ? batchId
            : null;
        var content = NormalizeContent(request.Document.Content, request.Document.ContentEncoding);

        var nodes = new Dictionary<string, GraphIngestionNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, GraphIngestionEdge>(StringComparer.OrdinalIgnoreCase);

        var userNodeId = $"user:{StableHash(tenantId, userId ?? "tenant")}";
        AddNode(nodes, userNodeId, new[] { "user" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["privacy_class"] = "private",
            ["observed_utc"] = observedUtc
        });

        var sourceNodeId = $"{sourceProfile.NodePrefix}:{StableHash(tenantId, sourceId)}";
        AddNode(nodes, sourceNodeId, new[] { sourceProfile.NodeLabel, "source" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["source_id"] = sourceId,
            ["source_document"] = sourceId,
            ["title"] = request.Document.Title,
            ["upload_batch_id"] = uploadBatchId,
            ["source_type"] = sourceProfile.SourceType,
            ["source_pack_target"] = sourceProfile.SourcePackTarget,
            ["target_pack_target"] = "cwi_memory_graph",
            ["provenance_id"] = traceId,
            ["privacy_class"] = "private",
            ["observed_utc"] = observedUtc
        });
        AddEdge(edges, $"edge:{StableHash(userNodeId, sourceNodeId, "IMPORTED_SOURCE")}", userNodeId, sourceNodeId, "IMPORTED_SOURCE", new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["source_document"] = sourceId,
            ["provenance_id"] = traceId,
            ["evidence_kind"] = "direct",
            ["observed_utc"] = observedUtc
        });

        foreach (var fact in ExtractFacts(content).Take(MaxFactsPerDocument))
        {
            var factId = $"memory_fact:{StableHash(tenantId, userId ?? "", sourceId, fact.Text, fact.Category)}";
            AddNode(nodes, factId, new[] { "memory_fact", $"memory_{fact.Domain}", $"category_{fact.Category}" }, new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["user_id"] = userId,
                ["source_id"] = sourceId,
                ["source_document"] = sourceId,
                ["source_pack_target"] = sourceProfile.SourcePackTarget,
                ["target_pack_target"] = "cwi_memory_graph",
                ["memory_domain"] = fact.Domain,
                ["memory_category"] = fact.Category,
                ["privacy_class"] = fact.PrivacyClass,
                ["fact_text"] = fact.Text,
                ["provenance_id"] = traceId,
                ["evidence_kind"] = "direct",
                ["observed_utc"] = observedUtc
            });

            AddEdge(edges, $"edge:{StableHash(userNodeId, factId, "HAS_MEMORY")}", userNodeId, factId, "HAS_MEMORY", new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["user_id"] = userId,
                ["memory_category"] = fact.Category,
                ["memory_domain"] = fact.Domain,
                ["source_document"] = sourceId,
                ["provenance_id"] = traceId,
                ["evidence_kind"] = "direct",
                ["observed_utc"] = observedUtc
            });
            AddEdge(edges, $"edge:{StableHash(sourceNodeId, factId, "CONTAINS_MEMORY")}", sourceNodeId, factId, "CONTAINS_MEMORY", new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["user_id"] = userId,
                ["source_document"] = sourceId,
                ["provenance_id"] = traceId,
                ["evidence_kind"] = "direct",
                ["observed_utc"] = observedUtc
            });
        }

        return new GraphIngestionProjection(nodes.Values.ToArray(), edges.Values.ToArray());
    }

    private static IEnumerable<MemoryFactCandidate> ExtractFacts(string content)
    {
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var normalizedLine = NormalizeFactText(SpeakerPrefixRegex.Replace(line, string.Empty));
            if (normalizedLine.Length == 0)
            {
                continue;
            }

            foreach (var sentence in SplitLongLine(normalizedLine))
            {
                var text = NormalizeFactText(sentence);
                if (ShouldSkipActiveMemoryFact(text))
                {
                    continue;
                }

                if (!LooksLikeMemoryFact(text))
                {
                    continue;
                }

                var domain = ResolveDomain(text);
                var category = ResolveCategory(text, domain);
                yield return new MemoryFactCandidate(text, domain, category, ResolvePrivacyClass(domain));
            }
        }
    }

    private static IEnumerable<string> SplitLongLine(string line)
    {
        if (line.Length <= 360)
        {
            yield return line;
            yield break;
        }

        foreach (var sentence in SentenceSplitRegex.Split(line))
        {
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                yield return sentence;
            }
        }
    }

    private static bool LooksLikeMemoryFact(string text)
    {
        if (text.Length < 18 || text.Length > 900)
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return ContainsAny(normalized, PersonalTerms)
               || ContainsAny(normalized, WorkTerms)
               || ContainsAny(normalized, OpenLoopTerms)
               || ContainsAny(normalized, GoalTerms)
               || ContainsAny(normalized, DecisionTerms)
               || ContainsAny(normalized, ResponsibilityTerms)
               || normalized.Contains(" i ", StringComparison.Ordinal)
               || normalized.StartsWith("i ", StringComparison.Ordinal)
               || normalized.Contains(" my ", StringComparison.Ordinal);
    }

    private static bool ShouldSkipActiveMemoryFact(string text)
    {
        return ContainsAny(text.ToLowerInvariant(), ExcludedActiveMemoryTerms)
               || LooksLikeTechnicalExportArtifact(text);
    }

    private static bool LooksLikeTechnicalExportArtifact(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (ContainsAny(normalized, TechnicalExportTerms))
        {
            return true;
        }

        return TechnicalJsonPreferenceRegex.IsMatch(normalized)
               && (normalized.Contains(":", StringComparison.Ordinal)
                   || normalized.Contains("true", StringComparison.Ordinal)
                   || normalized.Contains("false", StringComparison.Ordinal));
    }

    private static string ResolveDomain(string text)
    {
        var normalized = text.ToLowerInvariant();
        var personalScore = CountMatches(normalized, PersonalTerms);
        var workScore = CountMatches(normalized, WorkTerms);
        if (workScore > personalScore)
        {
            return "work";
        }

        if (personalScore > 0)
        {
            return "personal";
        }

        return "general";
    }

    private static string ResolveCategory(string text, string domain)
    {
        var normalized = text.ToLowerInvariant();
        if (ContainsAny(normalized, OpenLoopTerms))
        {
            return "open_loop";
        }

        if (ContainsAny(normalized, GoalTerms))
        {
            return "goal";
        }

        if (ContainsAny(normalized, DecisionTerms))
        {
            return "decision";
        }

        if (ContainsAny(normalized, ResponsibilityTerms))
        {
            return "responsibility";
        }

        if (normalized.Contains("working on", StringComparison.Ordinal)
            || normalized.Contains("worked on", StringComparison.Ordinal)
            || normalized.Contains("building", StringComparison.Ordinal)
            || normalized.Contains("implementing", StringComparison.Ordinal)
            || normalized.Contains("fixing", StringComparison.Ordinal)
            || normalized.Contains("project", StringComparison.Ordinal))
        {
            return "current_work";
        }

        return domain switch
        {
            "personal" => "home_life",
            "work" => "work_life",
            _ => "general_memory"
        };
    }

    private static string ResolvePrivacyClass(string domain)
    {
        return string.Equals(domain, "work", StringComparison.OrdinalIgnoreCase) ? "work" : "personal";
    }

    private static SourceProfile ResolveSourceProfile(IngestDocumentRequestDto request)
    {
        if (IsChatHistoryImport(request))
        {
            return new SourceProfile(
                "chat_history_import",
                "chat_import_source",
                "chat_import_source",
                "chatgpt_import");
        }

        var sourceType = TryGetMetadataString(request.Document.Metadata, "source_type", out var explicitSourceType)
            ? explicitSourceType
            : "uploaded_context";
        return new SourceProfile(
            sourceType,
            "uploaded_context_source",
            "uploaded_context_source",
            "context_upload");
    }


    private static void AddNode(
        IDictionary<string, GraphIngestionNode> nodes,
        string nodeId,
        IReadOnlyList<string> labels,
        IReadOnlyDictionary<string, object?> properties)
    {
        nodes[nodeId] = new GraphIngestionNode(nodeId, labels, properties);
    }

    private static void AddEdge(
        IDictionary<string, GraphIngestionEdge> edges,
        string edgeId,
        string fromNodeId,
        string toNodeId,
        string relation,
        IReadOnlyDictionary<string, object?> properties)
    {
        edges[edgeId] = new GraphIngestionEdge(edgeId, fromNodeId, toNodeId, relation, properties);
    }

    private static bool TryGetMetadataString(IReadOnlyDictionary<string, object?> metadata, string key, out string value)
    {
        value = string.Empty;
        if (!metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        value = raw switch
        {
            string text => text.Trim(),
            System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.String => element.GetString()?.Trim() ?? string.Empty,
            _ => raw.ToString()?.Trim() ?? string.Empty
        };
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeContent(string content, string? encoding)
    {
        return string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(content))
            : content;
    }

    private static string NormalizeFactText(string value)
    {
        var normalized = string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        while (normalized.StartsWith("- ", StringComparison.Ordinal)
               || normalized.StartsWith("* ", StringComparison.Ordinal)
               || normalized.StartsWith("• ", StringComparison.Ordinal))
        {
            normalized = normalized[2..].TrimStart();
        }

        return normalized;
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountMatches(string value, IReadOnlyList<string> terms)
    {
        return terms.Count(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string StableHash(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }

    private sealed record MemoryFactCandidate(
        string Text,
        string Domain,
        string Category,
        string PrivacyClass);

    private sealed record SourceProfile(
        string SourceType,
        string NodePrefix,
        string NodeLabel,
        string SourcePackTarget);
}
#nullable disable
