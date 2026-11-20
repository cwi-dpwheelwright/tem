using System.Text;
using System.Text.RegularExpressions;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class GraphQueryPlanner
{
    private readonly IReadOnlyList<IGraphQueryTemplateRule> _templateRules;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "into", "what", "which", "that", "this", "these", "those",
        "are", "is", "was", "were", "be", "been", "being", "about", "based", "using", "also", "appear",
        "appears", "supports", "supported", "require", "requires", "required", "attention", "evidence",
        "manual", "manuals", "reference", "references", "records", "record", "knowledge", "document", "documents",
        "process", "areas", "area", "question"
    };

    public GraphQueryPlanner(IEnumerable<IGraphQueryTemplateRule>? templateRules = null)
    {
        var configuredRules = templateRules?
            .OrderBy(rule => rule.Order)
            .ToArray();

        _templateRules = configuredRules is { Length: > 0 }
            ? configuredRules
            : GraphQueryTemplateRules.Defaults;
    }

    public GraphQueryPlan? TryPlan(string question, int topK, string? tenantId = null, string? userId = null)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var terms = ExtractTerms(question);
        var rule = DetectRule(question);
        if (rule is null)
        {
            return null;
        }

        if (rule.UsesUserMemory)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return null;
            }

            return new GraphQueryPlan
            {
                Template = rule.Template,
                Query = BuildUserMemoryQuery(tenantId.Trim(), userId, topK, rule.CategoryFilter),
                Terms = rule.DefaultTerms
            };
        }

        return rule.Template switch
        {
            "maintenance_attention" => new GraphQueryPlan
            {
                Template = rule.Template,
                Query = BuildMaintenanceQuery(terms, topK),
                Terms = terms
            },
            "procedure_family" => new GraphQueryPlan
            {
                Template = rule.Template,
                Query = BuildProcedureQuery(terms, topK),
                Terms = terms
            },
            _ => new GraphQueryPlan
            {
                Template = "equipment_metrics",
                Query = BuildEquipmentMetricsQuery(terms, topK),
                Terms = terms
            }
        };
    }

    private IGraphQueryTemplateRule? DetectRule(string question)
    {
        var lower = question.Trim().ToLowerInvariant();
        return _templateRules.FirstOrDefault(rule => rule.Matches(lower));
    }

    private static IReadOnlyList<string> ExtractTerms(string question)
    {
        return Regex.Matches(question.ToLowerInvariant(), "[a-z0-9][a-z0-9_-]{2,}")
            .Select(match => match.Value)
            .Where(term => !StopWords.Contains(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string BuildEquipmentMetricsQuery(IReadOnlyList<string> terms, int topK)
    {
        var filter = BuildFilterClause(
            new[]
            {
                "toLower(coalesce(e.name, e.node_id, e.id, ''))",
                "toLower(coalesce(m.name, m.node_id, m.id, ''))",
                "toLower(coalesce(r2.source_document, r1.source_document, ''))"
            },
            terms);

        return $$"""
MATCH (e:equipment)-[r2:MEASURED_BY]->(m:metric)
OPTIONAL MATCH (e)-[r1:MONITORED_IN]->(:equipment)
{{filter}}
RETURN
  coalesce(e.name, e.node_id, e.id, 'unknown_equipment') AS subject,
  'MEASURED_BY' AS relation,
  coalesce(m.name, m.node_id, m.id, 'unknown_metric') AS object_value,
  coalesce(r2.source_document, r1.source_document, '') AS source_document,
  coalesce(r2.source_pack_target, r1.source_pack_target, '') AS source_pack_target,
  coalesce(r2.target_pack_target, r1.target_pack_target, '') AS target_pack_target,
  coalesce(r1.document_class, r2.document_class, 'direct') AS evidence_kind
LIMIT {{Math.Max(topK, 1)}}
""";
    }

    private static string BuildMaintenanceQuery(IReadOnlyList<string> terms, int topK)
    {
        var filter = BuildFilterClause(
            new[]
            {
                "toLower(coalesce(e.name, e.node_id, e.id, ''))",
                "toLower(coalesce(target.name, target.node_id, target.id, ''))",
                "toLower(coalesce(r.source_document, ''))",
                "toLower(coalesce(r.maintenance_type, ''))"
            },
            terms);

        return $$"""
MATCH (e:equipment)-[r:MAINTAINED_IN]->(target)
{{filter}}
RETURN
  coalesce(e.name, e.node_id, e.id, 'unknown_equipment') AS subject,
  'MAINTAINED_IN' AS relation,
  coalesce(target.name, target.node_id, target.id, 'unknown_target') AS object_value,
  coalesce(r.source_document, '') AS source_document,
  coalesce(r.source_pack_target, '') AS source_pack_target,
  coalesce(r.target_pack_target, '') AS target_pack_target,
  coalesce(r.document_class, r.maintenance_type, 'direct') AS evidence_kind
LIMIT {{Math.Max(topK, 1)}}
""";
    }

    private static string BuildProcedureQuery(IReadOnlyList<string> terms, int topK)
    {
        var filter = BuildFilterClause(
            new[]
            {
                "toLower(coalesce(a.name, a.node_id, a.id, ''))",
                "toLower(coalesce(b.name, b.node_id, b.id, ''))",
                "toLower(coalesce(r.source_document, ''))"
            },
            terms);

        return $$"""
MATCH (a)-[r:PROCEDURE_FAMILY_LINK]->(b)
{{filter}}
RETURN
  coalesce(a.name, a.node_id, a.id, 'unknown_subject') AS subject,
  'PROCEDURE_FAMILY_LINK' AS relation,
  coalesce(b.name, b.node_id, b.id, 'unknown_target') AS object_value,
  coalesce(r.source_document, '') AS source_document,
  coalesce(r.source_pack_target, '') AS source_pack_target,
  coalesce(r.target_pack_target, '') AS target_pack_target,
  coalesce(r.document_class, 'direct') AS evidence_kind
LIMIT {{Math.Max(topK, 1)}}
""";
    }

    private static string BuildUserMemoryQuery(string tenantId, string? userId, int topK, string categoryFilter)
    {
        var privacyFilter = BuildUserMemoryPrivacyFilter(tenantId, userId);
        var filter = string.IsNullOrWhiteSpace(categoryFilter)
            ? privacyFilter
            : $"{privacyFilter} AND ({categoryFilter})";

        return $$"""
MATCH (u:user)-[:HAS_MEMORY]->(m)
WHERE {{filter}}
RETURN
  coalesce(m.memory_category, m.memory_type, m.memory_domain, 'memory') AS subject,
  'REMEMBERED' AS relation,
  coalesce(m.fact_text, '') AS object_value,
  coalesce(m.source_document, m.record_id, m.memory_id, '') AS source_document,
  coalesce(m.source_pack_target, 'chatgpt_import') AS source_pack_target,
  coalesce(m.target_pack_target, 'cwi_memory_graph') AS target_pack_target,
  coalesce(m.evidence_kind, 'direct') AS evidence_kind
ORDER BY coalesce(m.observed_utc, '') DESC
LIMIT {{Math.Max(topK, 1)}}
""";
    }

    private static string BuildUserMemoryPrivacyFilter(string tenantId, string? userId)
    {
        var tenant = EscapeCypherLiteral(tenantId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return $"coalesce(m.tenant_id, '') = '{tenant}' AND coalesce(m.user_id, '') = ''";
        }

        var user = EscapeCypherLiteral(userId.Trim());
        return $"coalesce(m.tenant_id, '') = '{tenant}' AND (coalesce(m.user_id, '') = '' OR coalesce(m.user_id, '') = '{user}')";
    }

    private static string BuildFilterClause(IReadOnlyList<string> fields, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("WHERE ");
        for (var i = 0; i < terms.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" OR ");
            }

            var term = EscapeCypherLiteral(terms[i]);
            builder.Append('(');
            for (var j = 0; j < fields.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(" OR ");
                }

                builder.Append(fields[j]);
                builder.Append(" CONTAINS '");
                builder.Append(term);
                builder.Append('\'');
            }
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string EscapeCypherLiteral(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }
}

