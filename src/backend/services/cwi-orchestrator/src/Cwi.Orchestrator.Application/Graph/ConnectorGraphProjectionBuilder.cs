#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class ConnectorGraphProjectionBuilder
{
    private static readonly Regex DeadlineRegex = new(
        @"\b(?:by|due|deadline(?:\s+on)?|before)\s+(?<value>today|tomorrow|monday|tuesday|wednesday|thursday|friday|saturday|sunday|[a-z]+\s+\d{1,2}(?:,\s*\d{4})?|\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ProjectRegex = new(
        @"\b(?:project|initiative|client|ticket)\s+(?<value>[a-z][a-z0-9_-]{1,60})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] RiskTerms =
    {
        "risk", "risky", "issue", "problem", "failure", "failed", "broken", "anomaly", "conflict"
    };

    private static readonly string[] BlockedTerms =
    {
        "blocked", "blocker", "blocking", "missing", "waiting on", "dependency"
    };

    private static readonly string[] GoalTerms =
    {
        "goal", "objective", "outcome", "target", "milestone"
    };

    private static readonly string[] DecisionTerms =
    {
        "decided", "decision", "agreed", "approved", "rejected", "chose", "chosen", "settled on"
    };

    public static bool IsConnectorNormalizedRecord(IngestDocumentRequestDto request)
    {
        return request.Tags.Any(tag => string.Equals(tag, "connector_normalized", StringComparison.OrdinalIgnoreCase))
            || TryGetMetadataString(request.Document.Metadata, "normalization_mode", out var mode)
               && string.Equals(mode, "connector_normalized_v1", StringComparison.OrdinalIgnoreCase);
    }

    public GraphIngestionProjection Build(IngestDocumentRequestDto request, string traceId)
    {
        if (!IsConnectorNormalizedRecord(request))
        {
            return new GraphIngestionProjection(Array.Empty<GraphIngestionNode>(), Array.Empty<GraphIngestionEdge>());
        }

        var tenantId = request.TenantId.Trim();
        var userId = request.UserId?.Trim() ?? string.Empty;
        var recordId = GetMetadataString(request, "record_id", request.Document.DocId.Trim());
        var connectorType = GetMetadataString(request, "connector_type", "connector");
        var sourceSystem = GetMetadataString(request, "source_system", connectorType);
        var accountId = GetMetadataString(request, "account_id", "default");
        var lifeWorkScope = GetMetadataString(request, "life_work_scope", "unknown");
        var sensitivityLabel = GetMetadataString(request, "sensitivity_label", "unknown_sensitive");
        var confidence = GetMetadataDouble(request, "confidence", 0.7);
        var sourceSha = GetMetadataString(request, "source_sha256", StableHash(recordId, request.Document.Content));
        var observedUtc = GetMetadataString(request, "event_time", (request.Document.CreatedUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O"));
        var domains = GetMetadataList(request, "candidate_memory_domains");
        if (domains.Count == 0)
        {
            domains = new[] { lifeWorkScope == "work" ? "work_career" : lifeWorkScope == "personal" ? "home" : "general" };
        }

        var memoryTypes = GetMetadataList(request, "candidate_memory_types");
        if (memoryTypes.Count == 0)
        {
            memoryTypes = new[] { "knowledge_memory" };
        }

        var factText = ExtractBody(request.Document.Content);
        var domain = domains[0];
        var memoryType = memoryTypes[0];
        var factKey = StableHash(userId, lifeWorkScope, domain, memoryType, NormalizeFactText(factText));
        var userNodeId = $"user:{StableHash(tenantId, userId)}";
        var sourceNodeId = $"source_record:{recordId}";
        var memoryNodeId = $"memory:{factKey[..24]}";
        var versionNodeId = $"memory_version:{StableHash(recordId, sourceSha)[..24]}";
        var domainNodeId = $"domain:{domain}";

        var nodes = new Dictionary<string, GraphIngestionNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, GraphIngestionEdge>(StringComparer.OrdinalIgnoreCase);

        AddNode(nodes, userNodeId, new[] { "user" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["privacy_class"] = lifeWorkScope,
            ["observed_utc"] = observedUtc
        });
        AddNode(nodes, sourceNodeId, new[] { "SourceRecord", "source_record", "connector_source_record" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["record_id"] = recordId,
            ["source_record_id"] = GetMetadataString(request, "source_record_id", recordId),
            ["connector_type"] = connectorType,
            ["source_system"] = sourceSystem,
            ["account_id"] = accountId,
            ["sensitivity_label"] = sensitivityLabel,
            ["life_work_scope"] = lifeWorkScope,
            ["source_sha256"] = sourceSha,
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        });
        AddNode(nodes, memoryNodeId, new[] { "Memory", "memory", $"memory_{memoryType}", $"domain_{domain}" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["memory_id"] = memoryNodeId,
            ["fact_key"] = factKey,
            ["memory_domain"] = domain,
            ["memory_type"] = memoryType,
            ["life_work_scope"] = lifeWorkScope,
            ["sensitivity_label"] = sensitivityLabel,
            ["confidence"] = confidence,
            ["fact_text"] = factText,
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        });
        AddNode(nodes, versionNodeId, new[] { "MemoryVersion", "memory_version" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["memory_id"] = memoryNodeId,
            ["record_id"] = recordId,
            ["source_sha256"] = sourceSha,
            ["confidence"] = confidence,
            ["fact_text"] = factText,
            ["version_status"] = "current",
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        });
        AddNode(nodes, domainNodeId, new[] { "Domain", "domain" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["domain"] = domain,
            ["life_work_scope"] = lifeWorkScope
        });

        AddEdge(edges, EdgeId(sourceNodeId, memoryNodeId, "PRODUCED"), sourceNodeId, memoryNodeId, "PRODUCED", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(memoryNodeId, versionNodeId, "CURRENT_VERSION"), memoryNodeId, versionNodeId, "CURRENT_VERSION", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(versionNodeId, sourceNodeId, "EVIDENCED_BY"), versionNodeId, sourceNodeId, "EVIDENCED_BY", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(memoryNodeId, domainNodeId, "BELONGS_TO_DOMAIN"), memoryNodeId, domainNodeId, "BELONGS_TO_DOMAIN", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(userNodeId, memoryNodeId, "HAS_MEMORY"), userNodeId, memoryNodeId, "HAS_MEMORY", EdgeProps(tenantId, userId, traceId, observedUtc));

        AddAdditionalDomainEdges(nodes, edges, tenantId, userId, traceId, observedUtc, lifeWorkScope, memoryNodeId, domainNodeId, domains);

        var entityNodeIdsByType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in ExtractEntities(request.Document.Content).Take(20))
        {
            var entityType = NormalizeGraphToken(entity.Type);
            var entityNodeId = $"entity:{StableHash(entityType, entity.Value)[..24]}";
            AddNode(nodes, entityNodeId, new[] { "Entity", "entity", $"entity_{entityType}" }, new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["user_id"] = userId,
                ["entity_type"] = entityType,
                ["value"] = entity.Value,
                ["confidence"] = entity.Confidence
            });
            AddEdge(edges, EdgeId(memoryNodeId, entityNodeId, "MENTIONS"), memoryNodeId, entityNodeId, "MENTIONS", EdgeProps(tenantId, userId, traceId, observedUtc));
            if (!entityNodeIdsByType.TryGetValue(entityType, out var ids))
            {
                ids = new List<string>();
                entityNodeIdsByType[entityType] = ids;
            }

            ids.Add(entityNodeId);
        }

        AddRelationshipProjection(
            nodes,
            edges,
            tenantId,
            userId,
            traceId,
            observedUtc,
            factText,
            domain,
            memoryType,
            lifeWorkScope,
            sensitivityLabel,
            confidence,
            factKey,
            userNodeId,
            memoryNodeId,
            domainNodeId,
            entityNodeIdsByType);

        return new GraphIngestionProjection(nodes.Values.ToArray(), edges.Values.ToArray());
    }

    private static void AddAdditionalDomainEdges(
        IDictionary<string, GraphIngestionNode> nodes,
        IDictionary<string, GraphIngestionEdge> edges,
        string tenantId,
        string userId,
        string traceId,
        string observedUtc,
        string lifeWorkScope,
        string memoryNodeId,
        string primaryDomainNodeId,
        IReadOnlyList<string> domains)
    {
        foreach (var additionalDomain in domains.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var additionalDomainNodeId = $"domain:{additionalDomain}";
            AddNode(nodes, additionalDomainNodeId, new[] { "Domain", "domain" }, new Dictionary<string, object?>
            {
                ["tenant_id"] = tenantId,
                ["domain"] = additionalDomain,
                ["life_work_scope"] = lifeWorkScope
            });
            AddEdge(edges, EdgeId(memoryNodeId, additionalDomainNodeId, "BELONGS_TO_DOMAIN"), memoryNodeId, additionalDomainNodeId, "BELONGS_TO_DOMAIN", EdgeProps(tenantId, userId, traceId, observedUtc));
            AddEdge(edges, EdgeId(primaryDomainNodeId, additionalDomainNodeId, "CROSSES_DOMAIN"), primaryDomainNodeId, additionalDomainNodeId, "CROSSES_DOMAIN", EdgeProps(tenantId, userId, traceId, observedUtc));
        }
    }

    private static void AddRelationshipProjection(
        IDictionary<string, GraphIngestionNode> nodes,
        IDictionary<string, GraphIngestionEdge> edges,
        string tenantId,
        string userId,
        string traceId,
        string observedUtc,
        string factText,
        string domain,
        string memoryType,
        string lifeWorkScope,
        string sensitivityLabel,
        double confidence,
        string factKey,
        string userNodeId,
        string memoryNodeId,
        string domainNodeId,
        IReadOnlyDictionary<string, List<string>> entityNodeIdsByType)
    {
        var taskNodeId = ShouldCreateTask(memoryType, factText)
            ? AddTaskNode(nodes, edges, tenantId, userId, traceId, observedUtc, factText, domain, memoryType, lifeWorkScope, sensitivityLabel, confidence, factKey, userNodeId, memoryNodeId)
            : null;

        var goalNodeId = ShouldCreateGoal(memoryType, factText)
            ? AddGoalNode(nodes, edges, tenantId, userId, traceId, observedUtc, factText, domain, lifeWorkScope, sensitivityLabel, confidence, factKey, userNodeId, memoryNodeId, domainNodeId)
            : null;

        if (!string.IsNullOrWhiteSpace(taskNodeId))
        {
            foreach (var personNodeId in EntityNodes(entityNodeIdsByType, "person"))
            {
                AddEdge(edges, EdgeId(personNodeId, taskNodeId, "HAS_TASK"), personNodeId, taskNodeId, "HAS_TASK", EdgeProps(tenantId, userId, traceId, observedUtc));
            }

            if (!string.IsNullOrWhiteSpace(goalNodeId))
            {
                AddEdge(edges, EdgeId(taskNodeId, goalNodeId, "SUPPORTS_GOAL"), taskNodeId, goalNodeId, "SUPPORTS_GOAL", EdgeProps(tenantId, userId, traceId, observedUtc));
            }

            var deadline = ExtractDeadline(factText);
            if (!string.IsNullOrWhiteSpace(deadline))
            {
                var deadlineNodeId = $"deadline:{StableHash(factKey, deadline)[..24]}";
                AddNode(nodes, deadlineNodeId, new[] { "Deadline", "deadline" }, new Dictionary<string, object?>
                {
                    ["tenant_id"] = tenantId,
                    ["user_id"] = userId,
                    ["deadline_text"] = deadline,
                    ["life_work_scope"] = lifeWorkScope,
                    ["observed_utc"] = observedUtc
                });
                AddEdge(edges, EdgeId(taskNodeId, deadlineNodeId, "DUE_ON"), taskNodeId, deadlineNodeId, "DUE_ON", EdgeProps(tenantId, userId, traceId, observedUtc));
            }

            if (ContainsAny(factText, BlockedTerms))
            {
                var issueNodeId = $"issue:{StableHash(factKey, "blocked")[..24]}";
                AddNode(nodes, issueNodeId, new[] { "Issue", "issue" }, RelationshipNodeProps(tenantId, userId, factText, domain, lifeWorkScope, sensitivityLabel, confidence, traceId, observedUtc));
                AddEdge(edges, EdgeId(taskNodeId, issueNodeId, "BLOCKED_BY"), taskNodeId, issueNodeId, "BLOCKED_BY", EdgeProps(tenantId, userId, traceId, observedUtc));
            }

            if (ContainsAny(factText, RiskTerms) || ContainsAny(factText, BlockedTerms))
            {
                var riskNodeId = $"risk:{StableHash(factKey, "risk")[..24]}";
                AddNode(nodes, riskNodeId, new[] { "Risk", "risk" }, RelationshipNodeProps(tenantId, userId, factText, domain, lifeWorkScope, sensitivityLabel, confidence, traceId, observedUtc));
                AddEdge(edges, EdgeId(taskNodeId, riskNodeId, "HAS_RISK"), taskNodeId, riskNodeId, "HAS_RISK", EdgeProps(tenantId, userId, traceId, observedUtc));
            }
        }

        if (ShouldCreateDecision(memoryType, factText))
        {
            var decisionNodeId = $"decision:{StableHash(factKey, "decision")[..24]}";
            AddNode(nodes, decisionNodeId, new[] { "Decision", "decision" }, RelationshipNodeProps(tenantId, userId, factText, domain, lifeWorkScope, sensitivityLabel, confidence, traceId, observedUtc));
            AddEdge(edges, EdgeId(memoryNodeId, decisionNodeId, "REPRESENTS"), memoryNodeId, decisionNodeId, "REPRESENTS", EdgeProps(tenantId, userId, traceId, observedUtc));

            var projectName = ExtractProjectName(factText);
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var projectNodeId = AddProjectNode(nodes, tenantId, userId, projectName, domain, lifeWorkScope, traceId, observedUtc);
                AddEdge(edges, EdgeId(decisionNodeId, projectNodeId, "AFFECTS"), decisionNodeId, projectNodeId, "AFFECTS", EdgeProps(tenantId, userId, traceId, observedUtc));
            }
        }

        if (taskNodeId is not null && !ShouldCreateGoal(memoryType, factText))
        {
            var projectName = ExtractProjectName(factText);
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var projectNodeId = AddProjectNode(nodes, tenantId, userId, projectName, domain, lifeWorkScope, traceId, observedUtc);
                AddEdge(edges, EdgeId(taskNodeId, projectNodeId, "AFFECTS"), taskNodeId, projectNodeId, "AFFECTS", EdgeProps(tenantId, userId, traceId, observedUtc));
            }
        }
    }

    private static string AddTaskNode(
        IDictionary<string, GraphIngestionNode> nodes,
        IDictionary<string, GraphIngestionEdge> edges,
        string tenantId,
        string userId,
        string traceId,
        string observedUtc,
        string factText,
        string domain,
        string memoryType,
        string lifeWorkScope,
        string sensitivityLabel,
        double confidence,
        string factKey,
        string userNodeId,
        string memoryNodeId)
    {
        var taskNodeId = $"task:{factKey[..24]}";
        AddNode(nodes, taskNodeId, new[] { "Task", "task", $"domain_{NormalizeGraphToken(domain)}" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["task_id"] = taskNodeId,
            ["fact_key"] = factKey,
            ["task_text"] = factText,
            ["memory_type"] = memoryType,
            ["memory_domain"] = domain,
            ["life_work_scope"] = lifeWorkScope,
            ["sensitivity_label"] = sensitivityLabel,
            ["confidence"] = confidence,
            ["status"] = "open",
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        });
        AddEdge(edges, EdgeId(userNodeId, taskNodeId, "HAS_TASK"), userNodeId, taskNodeId, "HAS_TASK", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(memoryNodeId, taskNodeId, "REPRESENTS"), memoryNodeId, taskNodeId, "REPRESENTS", EdgeProps(tenantId, userId, traceId, observedUtc));
        return taskNodeId;
    }

    private static string AddGoalNode(
        IDictionary<string, GraphIngestionNode> nodes,
        IDictionary<string, GraphIngestionEdge> edges,
        string tenantId,
        string userId,
        string traceId,
        string observedUtc,
        string factText,
        string domain,
        string lifeWorkScope,
        string sensitivityLabel,
        double confidence,
        string factKey,
        string userNodeId,
        string memoryNodeId,
        string domainNodeId)
    {
        var goalNodeId = $"goal:{StableHash(factKey, "goal")[..24]}";
        AddNode(nodes, goalNodeId, new[] { "Goal", "goal", $"domain_{NormalizeGraphToken(domain)}" }, RelationshipNodeProps(tenantId, userId, factText, domain, lifeWorkScope, sensitivityLabel, confidence, traceId, observedUtc));
        AddEdge(edges, EdgeId(userNodeId, goalNodeId, "HAS_GOAL"), userNodeId, goalNodeId, "HAS_GOAL", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(memoryNodeId, goalNodeId, "REPRESENTS"), memoryNodeId, goalNodeId, "REPRESENTS", EdgeProps(tenantId, userId, traceId, observedUtc));
        AddEdge(edges, EdgeId(goalNodeId, domainNodeId, "BELONGS_TO_DOMAIN"), goalNodeId, domainNodeId, "BELONGS_TO_DOMAIN", EdgeProps(tenantId, userId, traceId, observedUtc));
        return goalNodeId;
    }

    private static string AddProjectNode(
        IDictionary<string, GraphIngestionNode> nodes,
        string tenantId,
        string userId,
        string projectName,
        string domain,
        string lifeWorkScope,
        string traceId,
        string observedUtc)
    {
        var projectNodeId = $"project:{StableHash(tenantId, userId, projectName)[..24]}";
        AddNode(nodes, projectNodeId, new[] { "Project", "project", $"domain_{NormalizeGraphToken(domain)}" }, new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["project_name"] = projectName,
            ["memory_domain"] = domain,
            ["life_work_scope"] = lifeWorkScope,
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        });
        return projectNodeId;
    }

    private static Dictionary<string, object?> RelationshipNodeProps(
        string tenantId,
        string userId,
        string factText,
        string domain,
        string lifeWorkScope,
        string sensitivityLabel,
        double confidence,
        string traceId,
        string observedUtc)
    {
        return new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["fact_text"] = factText,
            ["memory_domain"] = domain,
            ["life_work_scope"] = lifeWorkScope,
            ["sensitivity_label"] = sensitivityLabel,
            ["confidence"] = confidence,
            ["provenance_id"] = traceId,
            ["observed_utc"] = observedUtc
        };
    }

    private static Dictionary<string, object?> EdgeProps(string tenantId, string userId, string traceId, string observedUtc)
    {
        return new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["provenance_id"] = traceId,
            ["evidence_kind"] = "connector_normalized",
            ["observed_utc"] = observedUtc
        };
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

    private static string EdgeId(string fromNodeId, string toNodeId, string relation)
    {
        return $"edge:{StableHash(fromNodeId, toNodeId, relation)[..24]}";
    }

    private static string ExtractBody(string content)
    {
        var match = Regex.Match(
            content,
            @"^\s*body:\s*\r?\n(?<body>.*?)(?=^\s*normalized_record_json:\s*|\z)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return NormalizeFactText(content);
        }

        return NormalizeFactText(match.Groups["body"].Value);
    }

    private static IReadOnlyList<(string Type, string Value, double Confidence)> ExtractEntities(string content)
    {
        var match = Regex.Match(
            content,
            @"^\s*normalized_record_json:\s*(?<json>\{.*\})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return Array.Empty<(string, string, double)>();
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(match.Groups["json"].Value);
            if (!document.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Array.Empty<(string, string, double)>();
            }

            return entities.EnumerateArray()
                .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.Object)
                .Select(item => (
                    Type: TryGetString(item, "type") ?? "entity",
                    Value: TryGetString(item, "value") ?? string.Empty,
                    Confidence: TryGetDouble(item, "confidence") ?? 0.75))
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .ToArray();
        }
        catch (System.Text.Json.JsonException)
        {
            return Array.Empty<(string, string, double)>();
        }
    }

    private static bool ShouldCreateTask(string memoryType, string factText)
    {
        return memoryType.Contains("task", StringComparison.OrdinalIgnoreCase)
               || factText.Contains("follow up", StringComparison.OrdinalIgnoreCase)
               || factText.Contains("to do", StringComparison.OrdinalIgnoreCase)
               || factText.Contains("need to", StringComparison.OrdinalIgnoreCase)
               || factText.Contains("due", StringComparison.OrdinalIgnoreCase)
               || factText.Contains("deadline", StringComparison.OrdinalIgnoreCase)
               || ContainsAny(factText, BlockedTerms);
    }

    private static bool ShouldCreateGoal(string memoryType, string factText)
    {
        return memoryType.Contains("goal", StringComparison.OrdinalIgnoreCase)
               || ContainsAny(factText, GoalTerms);
    }

    private static bool ShouldCreateDecision(string memoryType, string factText)
    {
        return memoryType.Contains("decision", StringComparison.OrdinalIgnoreCase)
               || ContainsAny(factText, DecisionTerms);
    }

    private static string? ExtractDeadline(string factText)
    {
        var match = DeadlineRegex.Match(factText);
        return match.Success ? match.Groups["value"].Value.Trim().TrimEnd('.', ',', ';', ':') : null;
    }

    private static string? ExtractProjectName(string factText)
    {
        var match = ProjectRegex.Match(factText);
        return match.Success ? match.Groups["value"].Value.Trim().TrimEnd('.', ',', ';', ':') : null;
    }

    private static IEnumerable<string> EntityNodes(
        IReadOnlyDictionary<string, List<string>> entityNodeIdsByType,
        string entityType)
    {
        return entityNodeIdsByType.TryGetValue(entityType, out var nodes)
            ? nodes
            : Array.Empty<string>();
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetMetadataList(IngestDocumentRequestDto request, string key)
    {
        if (!request.Document.Metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return Array.Empty<string>();
        }

        return raw switch
        {
            string text => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            IEnumerable<string> items => items.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private static string GetMetadataString(IngestDocumentRequestDto request, string key, string fallback)
    {
        return TryGetMetadataString(request.Document.Metadata, key, out var value) ? value : fallback;
    }

    private static double GetMetadataDouble(IngestDocumentRequestDto request, string key, double fallback)
    {
        if (!request.Document.Metadata.TryGetValue(key, out var raw) || raw is null)
        {
            return fallback;
        }

        return raw switch
        {
            double value => value,
            float value => value,
            decimal value => (double)value,
            string text when double.TryParse(text, out var parsed) => parsed,
            _ => fallback
        };
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

    private static string? TryGetString(System.Text.Json.JsonElement element, string key)
    {
        return element.TryGetProperty(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static double? TryGetDouble(System.Text.Json.JsonElement element, string key)
    {
        return element.TryGetProperty(key, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.Number
            && value.TryGetDouble(out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeFactText(string value)
    {
        return string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeGraphToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var normalized = string.Join('_', builder.ToString()
            .Split('_', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "entity" : normalized;
    }

    private static string StableHash(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
