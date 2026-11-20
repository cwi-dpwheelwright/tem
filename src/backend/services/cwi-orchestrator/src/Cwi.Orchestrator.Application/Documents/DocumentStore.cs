#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;

namespace Cwi.Orchestrator.Application.Documents;

public sealed class DocumentStore
{
    private const int SearchSnippetMaxLength = 1200;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "for", "from", "how", "i", "in",
        "is", "it", "me", "of", "on", "or", "our", "should", "so", "tell", "that", "the", "this", "to",
        "use", "we", "what", "when", "where", "with", "you"
    };

    private readonly ConcurrentDictionary<string, List<DocumentRecord>> _documentsByTenant =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public DocumentStore()
    {
        if (IsDemoDataEnabled())
        {
            Seed();
        }
    }

    private static bool IsDemoDataEnabled()
    {
        var raw = Environment.GetEnvironmentVariable("CWI_ENABLE_DEMO_DATA");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        raw = raw.Trim();
        return raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1";
    }

    public IReadOnlyList<DocumentSummaryDto> List(string tenantId, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Array.Empty<DocumentSummaryDto>();
        }

        if (!_documentsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return Array.Empty<DocumentSummaryDto>();
        }

        return records
            .Where(doc => CanAccess(doc, userId))
            .OrderBy(doc => doc.Title)
            .Select(ToSummary)
            .ToList();
    }

    public DocumentDetailDto? Get(string tenantId, string docId, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(docId))
        {
            return null;
        }

        if (!_documentsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return null;
        }

        var record = records.FirstOrDefault(doc =>
            string.Equals(doc.DocId, docId, StringComparison.OrdinalIgnoreCase)
            && CanAccess(doc, userId));
        return record is null ? null : ToDetail(record);
    }

    public IReadOnlyList<RetrievalResult> Search(string tenantId, string? userId, string query, int topK)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(query) || topK <= 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        if (!_documentsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return Array.Empty<RetrievalResult>();
        }

        var queryTokens = Tokenize(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTokens.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        return records
            .Where(doc => CanAccess(doc, userId))
            .Select(doc => BuildSearchResult(doc, queryTokens))
            .Where(result => result is not null)
            .Select(result => result!)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.DocId, StringComparer.OrdinalIgnoreCase)
            .Take(topK)
            .ToList();
    }

    public DocumentRollbackResponseDto? Rollback(
        string tenantId,
        string docId,
        string? userId,
        DocumentRollbackRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(docId))
        {
            return null;
        }

        if (!_documentsByTenant.TryGetValue(tenantId.Trim(), out var records))
        {
            return null;
        }

        lock (_gate)
        {
            var record = records.FirstOrDefault(doc =>
                string.Equals(doc.DocId, docId, StringComparison.OrdinalIgnoreCase)
                && CanAccess(doc, userId));
            if (record is null)
            {
                return null;
            }

            var target = record.Versions.FirstOrDefault(version =>
                string.Equals(version.Version, request.TargetVersion, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return new DocumentRollbackResponseDto
                {
                    RollbackId = Guid.NewGuid().ToString("N"),
                    DocId = record.DocId,
                    TargetVersion = request.TargetVersion,
                    Status = "not_found",
                    RequestedBy = request.ActorId,
                    RequestedUtc = DateTimeOffset.UtcNow,
                    Message = "Target version not found."
                };
            }

            var rollbackId = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var versionId = $"{target.Version}-rollback-{now:yyyyMMddHHmmss}";
            record.Status = "active";
            record.Versions.Insert(0, new DocumentVersionRecord
            {
                Version = versionId,
                CreatedUtc = now,
                Author = request.ActorId,
                Checksum = target.Checksum,
                Notes = request.Reason ?? $"Rollback to {target.Version}",
                Content = target.Content
            });

            return new DocumentRollbackResponseDto
            {
                RollbackId = rollbackId,
                DocId = record.DocId,
                TargetVersion = target.Version,
                Status = "staged",
                RequestedBy = request.ActorId,
                RequestedUtc = now,
                Message = $"Rollback staged for {target.Version}"
            };
        }
    }

    public void RecordIngest(IngestDocumentRequestDto request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TenantId))
        {
            return;
        }

        var tenantId = request.TenantId.Trim();
        var payload = request.Document;
        if (payload is null || string.IsNullOrWhiteSpace(payload.DocId))
        {
            return;
        }

        var createdUtc = payload.CreatedUtc ?? DateTimeOffset.UtcNow;
        var version = ExtractVersion(request);
        var type = ExtractType(payload.DocId, payload.Metadata);
        var owner = payload.Source?.Trim();
        var userId = request.UserId?.Trim();
        var content = NormalizeContent(payload.Content, payload.ContentEncoding);
        var checksum = ComputeChecksum(content);
        var notes = ExtractNotes(payload.Metadata);

        lock (_gate)
        {
            var records = _documentsByTenant.GetOrAdd(tenantId, _ => new List<DocumentRecord>());
            var record = records.FirstOrDefault(doc =>
                string.Equals(doc.DocId, payload.DocId, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                record = new DocumentRecord
                {
                    TenantId = tenantId,
                    UserId = userId,
                    DocId = payload.DocId.Trim(),
                    Title = payload.Title?.Trim() ?? payload.DocId.Trim(),
                    Type = type,
                    Owner = string.IsNullOrWhiteSpace(owner) ? "unknown" : owner,
                    Status = ExtractStatus(payload.Metadata) ?? "active",
                    Versions = new List<DocumentVersionRecord>()
                };
                records.Add(record);
            }

            record.Title = payload.Title?.Trim() ?? record.Title;
            record.Type = type;
            record.UserId = userId;
            if (!string.IsNullOrWhiteSpace(owner))
            {
                record.Owner = owner;
            }

            var existing = record.Versions.FirstOrDefault(ver =>
                string.Equals(ver.Version, version, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                record.Versions.Insert(0, new DocumentVersionRecord
                {
                    Version = version,
                    CreatedUtc = createdUtc,
                    Author = ExtractAuthor(payload.Metadata),
                    Checksum = checksum,
                    Notes = notes,
                    Content = content
                });
            }
            else
            {
                existing.CreatedUtc = createdUtc;
                existing.Author = ExtractAuthor(payload.Metadata);
                existing.Checksum = checksum;
                existing.Notes = notes;
                existing.Content = content;
            }
        }
    }

    private void Seed()
    {
        var tenantId = "tenant-demo";
        _documentsByTenant[tenantId] = new List<DocumentRecord>
        {
            new()
            {
                TenantId = tenantId,
                DocId = "doc-proc-018",
                Title = "Zone 3 safety procedure",
                Type = "procedure",
                Owner = "Safety Office",
                Status = "active",
                Versions = new List<DocumentVersionRecord>
                {
                    new()
                    {
                        Version = "1.8.2",
                        CreatedUtc = DateTimeOffset.Parse("2026-02-01T10:20:00Z"),
                        Author = "ops.lead",
                        Checksum = "a18f2d7c",
                        Notes = "Aligned with new thermal thresholds.",
                        Content =
                            "Step 1: Confirm break-glass scope.\nStep 2: Validate sensor quorum (>= 3).\nStep 3: Lock mesh handoff.\nStep 4: Execute safety sweep.\nStep 5: Record operator confirmation."
                    },
                    new()
                    {
                        Version = "1.8.1",
                        CreatedUtc = DateTimeOffset.Parse("2026-01-25T16:55:00Z"),
                        Author = "ops.lead",
                        Checksum = "b7348c12",
                        Notes = "Expanded rollback checklist.",
                        Content =
                            "Step 1: Confirm break-glass scope.\nStep 2: Validate sensor quorum (>= 3).\nStep 3: Execute safety sweep.\nStep 4: Record operator confirmation.\nStep 5: Run rollback checklist."
                    },
                    new()
                    {
                        Version = "1.7.9",
                        CreatedUtc = DateTimeOffset.Parse("2026-01-15T09:32:00Z"),
                        Author = "ops.coordinator",
                        Checksum = "c8821b44",
                        Notes = "Baseline safety sweep procedure.",
                        Content =
                            "Step 1: Confirm break-glass scope.\nStep 2: Validate sensor quorum (>= 2).\nStep 3: Execute safety sweep.\nStep 4: Record operator confirmation."
                    }
                }
            },
            new()
            {
                TenantId = tenantId,
                DocId = "doc-policy-044",
                Title = "Telemetry privacy policy",
                Type = "policy",
                Owner = "Compliance",
                Status = "active",
                Versions = new List<DocumentVersionRecord>
                {
                    new()
                    {
                        Version = "2.3.0",
                        CreatedUtc = DateTimeOffset.Parse("2026-01-30T12:10:00Z"),
                        Author = "privacy.lead",
                        Checksum = "f2a93b11",
                        Notes = "Clarified sampling controls and retention.",
                        Content =
                            "Telemetry data is collected only with operator consent. Sampling rates must be configurable per tenant. Raw payloads are retained for 7 days. Aggregates retained for 180 days."
                    },
                    new()
                    {
                        Version = "2.2.4",
                        CreatedUtc = DateTimeOffset.Parse("2026-01-12T08:47:00Z"),
                        Author = "privacy.lead",
                        Checksum = "a90d4431",
                        Notes = "Added audit export guidance.",
                        Content =
                            "Telemetry data is collected only with operator consent. Raw payloads are retained for 7 days. Aggregates retained for 180 days. Audit exports require admin approval."
                    }
                }
            },
            new()
            {
                TenantId = tenantId,
                DocId = "doc-runbook-007",
                Title = "Mesh incident rollback runbook",
                Type = "runbook",
                Owner = "Ops",
                Status = "draft",
                Versions = new List<DocumentVersionRecord>
                {
                    new()
                    {
                        Version = "0.9.1",
                        CreatedUtc = DateTimeOffset.Parse("2026-02-02T09:40:00Z"),
                        Author = "ops.coordinator",
                        Checksum = "b92a3f44",
                        Notes = "Draft runbook for mesh rollback.",
                        Content =
                            "1. Pause new automation.\n2. Verify topology health.\n3. Select rollback anchor.\n4. Notify stakeholders.\n5. Resume after validation."
                    }
                }
            }
        };
    }

    private static DocumentSummaryDto ToSummary(DocumentRecord record)
    {
        var latest = record.Versions.OrderByDescending(ver => ver.CreatedUtc).FirstOrDefault();
        return new DocumentSummaryDto
        {
            DocId = record.DocId,
            Title = record.Title,
            Type = record.Type,
            Owner = record.Owner,
            Status = record.Status,
            VersionsCount = record.Versions.Count,
            LatestVersion = latest?.Version ?? string.Empty
        };
    }

    private static DocumentDetailDto ToDetail(DocumentRecord record)
    {
        var versions = record.Versions
            .OrderByDescending(ver => ver.CreatedUtc)
            .Select(ver => new DocumentVersionDto
            {
                Version = ver.Version,
                CreatedUtc = ver.CreatedUtc,
                Author = ver.Author,
                Checksum = ver.Checksum,
                Notes = ver.Notes,
                Content = ver.Content
            })
            .ToList();

        return new DocumentDetailDto
        {
            DocId = record.DocId,
            Title = record.Title,
            Type = record.Type,
            Owner = record.Owner,
            Status = record.Status,
            Versions = versions
        };
    }

    private static RetrievalResult? BuildSearchResult(DocumentRecord record, HashSet<string> queryTokens)
    {
        var latest = record.Versions.OrderByDescending(ver => ver.CreatedUtc).FirstOrDefault();
        if (latest is null || string.IsNullOrWhiteSpace(latest.Content))
        {
            return null;
        }

        var searchableText = $"{record.Title}\n{record.Type}\n{record.Owner}\n{latest.Notes}\n{latest.Content}";
        var tokens = Tokenize(searchableText).ToList();
        if (tokens.Count == 0)
        {
            return null;
        }

        var matches = tokens.Count(token => queryTokens.Contains(token));
        if (matches == 0)
        {
            return null;
        }

        var uniqueMatches = tokens
            .Where(token => queryTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var score = (float)((uniqueMatches * 0.7d) + (matches * 0.3d / Math.Max(1, tokens.Count)));
        return new RetrievalResult(record.DocId, latest.Version, BuildSnippet(latest.Content, queryTokens), score);
    }

    private static string BuildSnippet(string content, HashSet<string> queryTokens)
    {
        var trimmed = content.Trim();
        if (trimmed.Length <= SearchSnippetMaxLength)
        {
            return trimmed;
        }

        var matchIndex = queryTokens
            .Select(token => trimmed.IndexOf(token, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();
        var start = Math.Max(0, matchIndex - (SearchSnippetMaxLength / 3));
        if (start + SearchSnippetMaxLength > trimmed.Length)
        {
            start = Math.Max(0, trimmed.Length - SearchSnippetMaxLength);
        }

        var snippet = trimmed.Substring(start, Math.Min(SearchSnippetMaxLength, trimmed.Length - start)).Trim();
        return start > 0 ? $"...{snippet}" : snippet;
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            foreach (var token in FlushToken(builder))
            {
                yield return token;
            }
        }

        foreach (var token in FlushToken(builder))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushToken(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length >= 3 && !StopWords.Contains(token))
        {
            yield return token;
        }
    }

    private static bool CanAccess(DocumentRecord record, string? userId)
    {
        if (string.IsNullOrWhiteSpace(record.UserId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(userId) &&
            string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractVersion(IngestDocumentRequestDto request)
    {
        var version = request.Version?.Trim();
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        if (request.Document?.Metadata is not null &&
            request.Document.Metadata.TryGetValue("version", out var metadataValue) &&
            metadataValue is string metadataVersion &&
            !string.IsNullOrWhiteSpace(metadataVersion))
        {
            return metadataVersion.Trim();
        }

        return "v1";
    }

    private static string ExtractType(string docId, IDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("type", out var rawType) && rawType is string typeValue && !string.IsNullOrWhiteSpace(typeValue))
        {
            return typeValue.Trim().ToLowerInvariant();
        }

        if (metadata.TryGetValue("doc_type", out var rawAlt) && rawAlt is string altValue && !string.IsNullOrWhiteSpace(altValue))
        {
            return altValue.Trim().ToLowerInvariant();
        }

        var normalized = docId.ToLowerInvariant();
        if (normalized.Contains("policy"))
        {
            return "policy";
        }

        if (normalized.Contains("runbook"))
        {
            return "runbook";
        }

        return "procedure";
    }

    private static string ExtractAuthor(IDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("author", out var raw) && raw is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (metadata.TryGetValue("owner", out var owner) && owner is string ownerValue && !string.IsNullOrWhiteSpace(ownerValue))
        {
            return ownerValue.Trim();
        }

        return "unknown";
    }

    private static string ExtractNotes(IDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("notes", out var raw) && raw is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (metadata.TryGetValue("comment", out var comment) && comment is string commentValue && !string.IsNullOrWhiteSpace(commentValue))
        {
            return commentValue.Trim();
        }

        return "Ingested document payload.";
    }

    private static string? ExtractStatus(IDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("status", out var raw) && raw is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToLowerInvariant();
        }

        return null;
    }

    private static string NormalizeContent(string content, string? encoding)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var bytes = Convert.FromBase64String(content);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        return content;
    }

    private static string ComputeChecksum(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..8];
    }

    private sealed class DocumentRecord
    {
        public string TenantId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string DocId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<DocumentVersionRecord> Versions { get; set; } = new();
    }

    private sealed class DocumentVersionRecord
    {
        public string Version { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public string Author { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
#nullable disable
