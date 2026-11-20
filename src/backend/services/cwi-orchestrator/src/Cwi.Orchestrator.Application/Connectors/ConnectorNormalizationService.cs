#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Connectors;

public sealed class ConnectorNormalizationService
{
    public const string SchemaVersion = "cwi.connector.normalized.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> AllowedSensitivityLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "public",
        "private_personal",
        "private_work",
        "shared_team",
        "restricted_health",
        "restricted_financial",
        "restricted_legal",
        "system_truth",
        "unknown_sensitive"
    };

    private static readonly HashSet<string> AllowedLifeWorkScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "personal",
        "work",
        "mixed",
        "system",
        "unknown"
    };

    private static readonly IReadOnlyDictionary<string, ConnectorDefaults> DefaultsByType =
        new Dictionary<string, ConnectorDefaults>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = new("private_work", "work", new[] { "work_career", "business", "social", "family", "finance", "legal_ip", "technology" }, new[] { "task_memory", "relationship_memory", "decision_memory", "issue_memory", "project_memory", "event_memory", "knowledge_memory" }, 30),
            ["messages"] = new("private_work", "work", new[] { "work_career", "business", "social", "family", "technology" }, new[] { "task_memory", "relationship_memory", "decision_memory", "issue_memory", "project_memory", "event_memory" }, 30),
            ["calendar"] = new("private_work", "mixed", new[] { "work_career", "health_wellness", "family", "travel", "school_learning", "home" }, new[] { "event_memory", "routine_memory", "task_memory", "relationship_memory" }, 30),
            ["documents"] = new("private_work", "work", new[] { "work_career", "business", "legal_ip", "finance", "school_learning", "home", "technology" }, new[] { "knowledge_memory", "decision_memory", "project_memory", "workflow_memory", "legal_ip_memory", "financial_memory" }, null),
            ["billing"] = new("restricted_financial", "personal", new[] { "finance", "home", "business" }, new[] { "financial_memory", "task_memory", "routine_memory", "issue_memory", "goal_memory" }, 90),
            ["payments"] = new("restricted_financial", "personal", new[] { "finance", "home", "business" }, new[] { "financial_memory", "task_memory", "routine_memory", "issue_memory", "goal_memory" }, 90),
            ["banking"] = new("restricted_financial", "personal", new[] { "finance", "home", "business" }, new[] { "financial_memory", "task_memory", "routine_memory", "issue_memory", "goal_memory" }, 90),
            ["health"] = new("restricted_health", "personal", new[] { "health_wellness" }, new[] { "event_memory", "routine_memory", "personal_context_memory", "issue_memory" }, null),
            ["lab"] = new("restricted_health", "personal", new[] { "health_wellness" }, new[] { "event_memory", "routine_memory", "personal_context_memory", "issue_memory" }, null),
            ["shopping"] = new("private_personal", "personal", new[] { "home", "pets", "food_meals", "hobbies", "family" }, new[] { "preference_memory", "routine_memory", "task_memory", "event_memory", "financial_memory" }, 90),
            ["smart_home"] = new("private_personal", "personal", new[] { "home", "safety_security", "energy" }, new[] { "routine_memory", "event_memory", "issue_memory", "safety_security_memory" }, 7),
            ["vehicle"] = new("private_personal", "personal", new[] { "vehicles", "finance", "home" }, new[] { "task_memory", "event_memory", "financial_memory", "routine_memory" }, 90),
            ["school"] = new("private_personal", "personal", new[] { "school_learning", "family" }, new[] { "task_memory", "event_memory", "goal_memory", "knowledge_memory" }, 90),
            ["work_apps"] = new("private_work", "work", new[] { "work_career", "business", "technology", "operations", "legal_ip" }, new[] { "project_memory", "task_memory", "issue_memory", "decision_memory", "workflow_memory", "relationship_memory" }, 30)
        };

    private static readonly string[] PersonalIndicators =
    {
        "family", "home", "dentist", "doctor", "school", "house", "appointment", "pet", "dog", "cat",
        "bill", "bank", "vehicle", "car", "health", "lab", "shopping", "order"
    };

    private static readonly string[] WorkIndicators =
    {
        "project", "client", "vendor", "deadline", "ticket", "approval", "meeting", "work", "team",
        "manager", "demo", "deploy", "api", "cwi", "investor", "customer"
    };

    public ConnectorNormalizationResult Normalize(
        string connectorType,
        string tenantId,
        string userId,
        ConnectorWebhookRequestDto request,
        JsonElement payload,
        DateTimeOffset receivedUtc)
    {
        var records = request.NormalizedRecords is { Count: > 0 }
            ? request.NormalizedRecords
            : request.NormalizedRecord is not null
                ? new[] { request.NormalizedRecord }
                : new[] { BuildGenericRecord(connectorType, tenantId, userId, request, payload, receivedUtc) };

        var normalized = new List<NormalizedConnectorRecordDto>();
        var errors = new List<string>();
        foreach (var record in records)
        {
            var prepared = PrepareRecord(connectorType, tenantId, userId, request, record, payload, receivedUtc);
            var recordErrors = Validate(prepared, tenantId, userId, connectorType);
            if (recordErrors.Count > 0)
            {
                errors.AddRange(recordErrors);
                continue;
            }

            normalized.Add(prepared);
        }

        return new ConnectorNormalizationResult(normalized, errors);
    }

    public static string ComputeFactKey(NormalizedConnectorRecordDto record)
    {
        var domain = record.CandidateMemoryDomains.FirstOrDefault() ?? "general";
        var memoryType = record.CandidateMemoryTypes.FirstOrDefault() ?? "knowledge_memory";
        var factText = NormalizeText(record.Body ?? string.Empty);
        return StableHash(record.UserId, record.LifeWorkScope, domain, memoryType, factText);
    }

    public static string SourceSha256For(NormalizedConnectorRecordDto record)
    {
        if (!string.IsNullOrWhiteSpace(record.Provenance?.SourceSha256))
        {
            return record.Provenance.SourceSha256.Trim();
        }

        return StableHash(
            record.TenantId,
            record.UserId,
            record.AccountId,
            record.ConnectorType,
            record.SourceSystem,
            record.SourceRecordId,
            record.Title,
            record.Body,
            record.EventTime?.UtcDateTime.ToString("O"));
    }

    public static bool IsStale(NormalizedConnectorRecordDto record)
    {
        var type = record.ConnectorType ?? string.Empty;
        if (!DefaultsByType.TryGetValue(type, out var defaults) || defaults.FreshnessDays is null)
        {
            return false;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-defaults.FreshnessDays.Value);
        return record.EventTime is not null && record.EventTime.Value < cutoff;
    }

    public static string SerializeRecord(NormalizedConnectorRecordDto record)
    {
        return JsonSerializer.Serialize(record, JsonOptions);
    }

    private static NormalizedConnectorRecordDto PrepareRecord(
        string connectorType,
        string tenantId,
        string userId,
        ConnectorWebhookRequestDto request,
        NormalizedConnectorRecordDto record,
        JsonElement payload,
        DateTimeOffset receivedUtc)
    {
        var normalizedType = NormalizeToken(record.ConnectorType) ?? connectorType;
        var sourceSystem = FirstNonBlank(record.SourceSystem, request.SourceSystem, ReadString(payload, "source_system"), normalizedType);
        var accountId = FirstNonBlank(record.AccountId, request.AccountId, ReadString(payload, "account_id"), "default");
        var eventTime = record.EventTime ?? request.OccurredUtc ?? ReadDate(payload, "event_time") ?? ReadDate(payload, "created_at") ?? DateTimeOffset.UnixEpoch;
        var body = FirstNonBlank(record.Body, ReadString(payload, "body"), ReadString(payload, "content"), ExtractPayloadText(payload), string.Empty);
        var title = FirstNonBlank(record.Title, request.Title, ReadString(payload, "title"), ReadString(payload, "subject"), $"{normalizedType} record");
        var sourceRecordId = FirstNonBlank(
            record.SourceRecordId,
            request.SourceId,
            ReadString(payload, "source_record_id"),
            ReadString(payload, "id"),
            ReadString(payload, "message_id"),
            "derived:" + StableHash(title, body, eventTime.UtcDateTime.ToString("O")));
        var defaults = ResolveDefaults(normalizedType, title, body, sourceSystem);
        var domains = record.CandidateMemoryDomains.Count > 0
            ? record.CandidateMemoryDomains.Select(NormalizeToken).Where(item => item is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : defaults.Domains;
        var memoryTypes = record.CandidateMemoryTypes.Count > 0
            ? record.CandidateMemoryTypes.Select(NormalizeToken).Where(item => item is not null).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : defaults.MemoryTypes;
        var sensitivity = NormalizeToken(record.SensitivityLabel) ?? ResolveSensitivity(normalizedType, title, body, defaults.SensitivityLabel);
        var lifeWorkScope = NormalizeToken(record.LifeWorkScope) ?? ResolveLifeWorkScope(normalizedType, title, body, defaults.LifeWorkScope);
        var rawExcerpt = FirstNonBlank(record.RawExcerpt, ReadString(payload, "raw_excerpt"), body);
        if (rawExcerpt.Length > 280)
        {
            rawExcerpt = rawExcerpt[..280];
        }

        var preliminary = new NormalizedConnectorRecordDto
        {
            SchemaVersion = FirstNonBlank(record.SchemaVersion, SchemaVersion),
            RecordId = record.RecordId,
            TenantId = FirstNonBlank(record.TenantId, tenantId),
            UserId = FirstNonBlank(record.UserId, userId),
            AccountId = accountId,
            ConnectorType = normalizedType,
            SourceSystem = sourceSystem,
            SourceRecordId = sourceRecordId,
            SourceThreadId = FirstNonBlank(record.SourceThreadId, request.SourceThreadId, ReadString(payload, "source_thread_id"), ReadString(payload, "thread_id")),
            SourceUrl = FirstNonBlank(record.SourceUrl, request.SourceUrl, ReadString(payload, "source_url"), ReadString(payload, "web_url"), ReadString(payload, "url")),
            EventTime = eventTime,
            IngestedAt = record.IngestedAt ?? receivedUtc,
            Title = title,
            Body = body,
            RawExcerpt = rawExcerpt,
            Participants = record.Participants.Count > 0 ? record.Participants : ExtractParticipants(payload),
            Entities = record.Entities.Count > 0 ? record.Entities : ExtractEntities(payload),
            CandidateMemoryDomains = domains,
            CandidateMemoryTypes = memoryTypes,
            LifeWorkScope = lifeWorkScope,
            SensitivityLabel = sensitivity,
            Confidence = record.Confidence ?? 0.85,
            Provenance = record.Provenance ?? new ConnectorProvenanceDto
            {
                Provider = sourceSystem,
                ProviderRecordId = sourceRecordId,
                ProviderAccount = accountId,
                AdapterVersion = $"{sourceSystem}-{normalizedType}-adapter-v1.0.0",
                SyncJobId = FirstNonBlank(ReadString(payload, "sync_job_id"), request.EventType, "webhook"),
                RetrievedAt = receivedUtc
            },
            SyncState = record.SyncState ?? new ConnectorSyncStateDto
            {
                SyncType = FirstNonBlank(ReadString(payload, "sync_type"), "incremental"),
                Cursor = ReadString(payload, "cursor"),
                IsDeleted = ReadBool(payload, "is_deleted"),
                IsUpdate = ReadBool(payload, "is_update")
            }
        };

        var sourceSha = SourceSha256For(preliminary);
        var provenance = preliminary.Provenance ?? new ConnectorProvenanceDto();
        var withProvenance = new ConnectorProvenanceDto
        {
            Provider = FirstNonBlank(provenance.Provider, sourceSystem),
            ProviderRecordId = FirstNonBlank(provenance.ProviderRecordId, sourceRecordId),
            ProviderAccount = FirstNonBlank(provenance.ProviderAccount, accountId),
            SourceSha256 = sourceSha,
            AdapterVersion = FirstNonBlank(provenance.AdapterVersion, $"{sourceSystem}-{normalizedType}-adapter-v1.0.0"),
            SyncJobId = provenance.SyncJobId,
            RetrievedAt = provenance.RetrievedAt ?? receivedUtc
        };

        var recordId = FirstNonBlank(
            preliminary.RecordId,
            StableHash(preliminary.TenantId, preliminary.UserId, preliminary.AccountId, preliminary.ConnectorType, preliminary.SourceSystem, preliminary.SourceRecordId, preliminary.EventTime?.UtcDateTime.ToString("O")));

        return preliminary with
        {
            RecordId = recordId,
            Provenance = withProvenance
        };
    }

    private static NormalizedConnectorRecordDto BuildGenericRecord(
        string connectorType,
        string tenantId,
        string userId,
        ConnectorWebhookRequestDto request,
        JsonElement payload,
        DateTimeOffset receivedUtc)
    {
        return new NormalizedConnectorRecordDto
        {
            SchemaVersion = SchemaVersion,
            TenantId = tenantId,
            UserId = userId,
            AccountId = request.AccountId,
            ConnectorType = connectorType,
            SourceSystem = request.SourceSystem,
            SourceRecordId = request.SourceId,
            SourceThreadId = request.SourceThreadId,
            SourceUrl = request.SourceUrl,
            EventTime = request.OccurredUtc,
            IngestedAt = receivedUtc,
            Title = request.Title,
            Body = ReadString(payload, "body") ?? ReadString(payload, "content") ?? ExtractPayloadText(payload),
            Confidence = 0.85
        };
    }

    private static IReadOnlyList<string> Validate(
        NormalizedConnectorRecordDto record,
        string tenantId,
        string userId,
        string connectorType)
    {
        var errors = new List<string>();
        Require(record.SchemaVersion, "schema_version", errors);
        if (!string.Equals(record.SchemaVersion, SchemaVersion, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("schema_version must be cwi.connector.normalized.v1.");
        }

        Require(record.RecordId, "record_id", errors);
        Require(record.TenantId, "tenant_id", errors);
        Require(record.UserId, "user_id", errors);
        Require(record.AccountId, "account_id", errors);
        Require(record.ConnectorType, "connector_type", errors);
        Require(record.SourceSystem, "source_system", errors);
        Require(record.SourceRecordId, "source_record_id", errors);
        Require(record.Title, "title", errors);
        Require(record.Body, "body", errors);
        Require(record.LifeWorkScope, "life_work_scope", errors);
        Require(record.SensitivityLabel, "sensitivity_label", errors);

        if (record.EventTime is null)
        {
            errors.Add("event_time is required.");
        }

        if (record.IngestedAt is null)
        {
            errors.Add("ingested_at is required.");
        }

        if (record.Provenance is null)
        {
            errors.Add("provenance is required.");
        }

        if (record.SyncState is null)
        {
            errors.Add("sync_state is required.");
        }

        if (record.Confidence is null or < 0 or > 1)
        {
            errors.Add("confidence must be between 0 and 1.");
        }

        if (!string.IsNullOrWhiteSpace(record.TenantId)
            && !string.Equals(record.TenantId.Trim(), tenantId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("tenant_id does not match authenticated tenant.");
        }

        if (!string.IsNullOrWhiteSpace(record.UserId)
            && !string.Equals(record.UserId.Trim(), userId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("user_id does not match authenticated user.");
        }

        if (!string.IsNullOrWhiteSpace(record.ConnectorType)
            && !string.Equals(record.ConnectorType.Trim(), connectorType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("connector_type does not match webhook route.");
        }

        if (!string.IsNullOrWhiteSpace(record.SensitivityLabel)
            && !AllowedSensitivityLabels.Contains(record.SensitivityLabel.Trim()))
        {
            errors.Add("sensitivity_label is not allowed.");
        }

        if (!string.IsNullOrWhiteSpace(record.LifeWorkScope)
            && !AllowedLifeWorkScopes.Contains(record.LifeWorkScope.Trim()))
        {
            errors.Add("life_work_scope is not allowed.");
        }

        return errors;
    }

    private static ConnectorDefaults ResolveDefaults(string connectorType, string title, string body, string sourceSystem)
    {
        var key = connectorType switch
        {
            "outlook" or "gmail" => "email",
            "teams" or "slack" => "messages",
            "lab_work" => "lab",
            _ => connectorType
        };

        if (!DefaultsByType.TryGetValue(key, out var defaults))
        {
            defaults = new ConnectorDefaults("unknown_sensitive", "unknown", new[] { "general" }, new[] { "knowledge_memory" }, 30);
        }

        if (LooksLegal(title, body))
        {
            return defaults with
            {
                SensitivityLabel = "restricted_legal",
                Domains = defaults.Domains.Concat(new[] { "legal_ip" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                MemoryTypes = defaults.MemoryTypes.Concat(new[] { "legal_ip_memory" }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        return defaults;
    }

    private static string ResolveSensitivity(string connectorType, string title, string body, string fallback)
    {
        if (connectorType is "health" or "lab" or "lab_work")
        {
            return "restricted_health";
        }

        if (connectorType is "billing" or "banking" or "payments" or "finance")
        {
            return "restricted_financial";
        }

        if (LooksLegal(title, body))
        {
            return "restricted_legal";
        }

        return fallback;
    }

    private static string ResolveLifeWorkScope(string connectorType, string title, string body, string fallback)
    {
        if (connectorType is "health" or "lab" or "shopping" or "smart_home" or "vehicle" or "school" or "billing" or "banking" or "payments")
        {
            return "personal";
        }

        if (connectorType is "work_apps")
        {
            return "work";
        }

        var text = $"{title} {body}".ToLowerInvariant();
        var workScore = WorkIndicators.Count(indicator => text.Contains(indicator, StringComparison.Ordinal));
        var personalScore = PersonalIndicators.Count(indicator => text.Contains(indicator, StringComparison.Ordinal));
        if (workScore > 0 && personalScore > 0)
        {
            return "mixed";
        }

        if (workScore > personalScore)
        {
            return "work";
        }

        if (personalScore > workScore)
        {
            return "personal";
        }

        return fallback;
    }

    private static bool LooksLegal(string title, string body)
    {
        var text = $"{title} {body}".ToLowerInvariant();
        return text.Contains("contract", StringComparison.Ordinal)
            || text.Contains("legal", StringComparison.Ordinal)
            || text.Contains("renewal", StringComparison.Ordinal)
            || text.Contains("agreement", StringComparison.Ordinal);
    }

    private static IReadOnlyList<ConnectorParticipantDto> ExtractParticipants(JsonElement payload)
    {
        if (!payload.TryGetProperty("participants", out var participants) || participants.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ConnectorParticipantDto>();
        }

        return participants.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ConnectorParticipantDto
            {
                Name = ReadString(item, "name"),
                Email = ReadString(item, "email"),
                Role = ReadString(item, "role")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name) || !string.IsNullOrWhiteSpace(item.Email))
            .ToArray();
    }

    private static IReadOnlyList<ConnectorEntityDto> ExtractEntities(JsonElement payload)
    {
        if (!payload.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ConnectorEntityDto>();
        }

        return entities.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ConnectorEntityDto
            {
                Type = NormalizeToken(ReadString(item, "type")) ?? "entity",
                Value = ReadString(item, "value"),
                Confidence = ReadDouble(item, "confidence") ?? 0.75
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToArray();
    }

    private static string ExtractPayloadText(JsonElement payload)
    {
        var output = new List<string>();
        ExtractText(payload, output, depth: 0);
        return string.Join("\n", output.Take(120));
    }

    private static void ExtractText(JsonElement element, ICollection<string> output, int depth)
    {
        if (depth > 6 || output.Count >= 160)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ConnectorSensitiveFieldPolicy.IsSensitiveKey(property.Name))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            output.Add($"{property.Name}: {value.Trim()}");
                        }
                    }
                    else
                    {
                        ExtractText(property.Value, output, depth + 1);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ExtractText(item, output, depth + 1);
                }
                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    output.Add(text.Trim());
                }
                break;
        }
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string? NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static void Require(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static string NormalizeText(string value)
    {
        return string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string StableHash(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ConnectorDefaults(
        string SensitivityLabel,
        string LifeWorkScope,
        IReadOnlyList<string> Domains,
        IReadOnlyList<string> MemoryTypes,
        int? FreshnessDays);
}

public sealed record ConnectorNormalizationResult(
    IReadOnlyList<NormalizedConnectorRecordDto> Records,
    IReadOnlyList<string> Errors);
#nullable disable
