using Cwi.Common;
using Cwi.Common.Errors;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Graph;

public interface IGraphEvidenceProvider
{
    Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken);
}

public sealed class AgeGraphEvidenceProvider : IGraphEvidenceProvider
{
    private readonly ApplicationOptions _options;
    private readonly GraphQueryPlanner _planner;
    private readonly ILogger<AgeGraphEvidenceProvider> _logger;

    public AgeGraphEvidenceProvider(
        ApplicationOptions options,
        GraphQueryPlanner planner,
        ILogger<AgeGraphEvidenceProvider> logger)
    {
        _options = options;
        _planner = planner;
        _logger = logger;
    }

    public async Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken)
    {
        if (!_options.GraphEnabled)
        {
            return ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult());
        }

        if (string.IsNullOrWhiteSpace(_options.GraphDbConnectionString))
        {
            return ServiceResponse<GraphQueryResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Graph DB connection missing."),
                statusCode: 503,
                message: "Graph DB unavailable");
        }

        var topK = request.TopK.GetValueOrDefault(_options.GraphTopK);
        var plan = _planner.TryPlan(request.Question, topK, request.TenantId, request.UserId);
        if (plan is null)
        {
            return ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult());
        }

        try
        {
            await using var connection = new NpgsqlConnection(_options.GraphDbConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandTimeout = _options.GraphQueryTimeoutSeconds;
            command.CommandText = BuildCommandText(plan.Query);

            var evidence = new List<GraphEvidence>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                evidence.Add(new GraphEvidence
                {
                    Subject = ReadRequiredString(reader, 0),
                    Relation = ReadRequiredString(reader, 1),
                    Object = ReadRequiredString(reader, 2),
                    SourceDocument = ReadOptionalString(reader, 3),
                    SourcePackTarget = ReadOptionalString(reader, 4),
                    TargetPackTarget = ReadOptionalString(reader, 5),
                    Template = plan.Template,
                    Inferred = IsInferred(ReadOptionalString(reader, 6))
                });
            }

            _logger.LogInformation(
                "Graph query {Template} returned {Count} row(s) for terms {Terms}.",
                plan.Template,
                evidence.Count,
                string.Join(",", plan.Terms));

            return ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult
            {
                Template = plan.Template,
                Terms = plan.Terms,
                Evidence = evidence
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Graph query failed for template {Template}.", plan.Template);
            return ServiceResponse<GraphQueryResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Graph query failed."),
                statusCode: 503,
                message: "Graph query failed");
        }
    }

    private string BuildCommandText(string query)
    {
        return $"""
LOAD 'age';
SET search_path = ag_catalog, "$user", public;
SELECT
  subject::text AS subject,
  relation::text AS relation,
  object_value::text AS object_value,
  source_document::text AS source_document,
  source_pack_target::text AS source_pack_target,
  target_pack_target::text AS target_pack_target,
  evidence_kind::text AS evidence_kind
FROM ag_catalog.cypher('{_options.GraphName}', $$ {query} $$) AS (
  subject agtype,
  relation agtype,
  object_value agtype,
  source_document agtype,
  source_pack_target agtype,
  target_pack_target agtype,
  evidence_kind agtype
);
""";
    }

    private static string ReadRequiredString(NpgsqlDataReader reader, int ordinal)
    {
        return NormalizeText(reader.GetString(ordinal)) ?? string.Empty;
    }

    private static string? ReadOptionalString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : NormalizeText(reader.GetString(ordinal));
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(trimmed);
            }
            catch (JsonException)
            {
                return trimmed[1..^1];
            }
        }

        return trimmed;
    }

    private static bool IsInferred(string? evidenceKind)
    {
        return string.Equals(evidenceKind, "crosspack_inference", StringComparison.OrdinalIgnoreCase)
            || string.Equals(evidenceKind, "inferred", StringComparison.OrdinalIgnoreCase);
    }
}
