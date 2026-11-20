#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Cwi.Orchestrator.Application.Graph;

public interface IImportedChatGraphIngestionService
{
    Task<ServiceResponse<GraphIngestionResult>> IngestAsync(
        IngestDocumentRequestDto request,
        string traceId,
        CancellationToken cancellationToken);
}

public sealed class AgeImportedChatGraphIngestionService : IImportedChatGraphIngestionService
{
    private readonly ApplicationOptions _options;
    private readonly ImportedChatGraphProjectionBuilder _projectionBuilder;
    private readonly ConnectorGraphProjectionBuilder _connectorProjectionBuilder;
    private readonly ILogger<AgeImportedChatGraphIngestionService> _logger;

    public AgeImportedChatGraphIngestionService(
        ApplicationOptions options,
        ImportedChatGraphProjectionBuilder projectionBuilder,
        ConnectorGraphProjectionBuilder connectorProjectionBuilder,
        ILogger<AgeImportedChatGraphIngestionService> logger)
    {
        _options = options;
        _projectionBuilder = projectionBuilder;
        _connectorProjectionBuilder = connectorProjectionBuilder;
        _logger = logger;
    }

    public async Task<ServiceResponse<GraphIngestionResult>> IngestAsync(
        IngestDocumentRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var isChatImport = ImportedChatGraphProjectionBuilder.IsChatHistoryImport(request);
        var isConnectorRecord = ConnectorGraphProjectionBuilder.IsConnectorNormalizedRecord(request);
        if (!isChatImport && !isConnectorRecord)
        {
            return ServiceResponse<GraphIngestionResult>.Success(new GraphIngestionResult
            {
                Skipped = true,
                Reason = "not_graph_ingestable_source"
            });
        }

        if (!_options.GraphEnabled || string.IsNullOrWhiteSpace(_options.GraphDbConnectionString))
        {
            return ServiceResponse<GraphIngestionResult>.Success(new GraphIngestionResult
            {
                Skipped = true,
                Reason = "graph_disabled_or_unavailable"
            });
        }

        var projection = isConnectorRecord
            ? _connectorProjectionBuilder.Build(request, traceId)
            : _projectionBuilder.Build(request, traceId);
        if (projection.Nodes.Count == 0 && projection.Edges.Count == 0)
        {
            return ServiceResponse<GraphIngestionResult>.Success(new GraphIngestionResult
            {
                Skipped = true,
                Reason = "no_graph_facts_extracted"
            });
        }

        try
        {
            await using var connection = new NpgsqlConnection(_options.GraphDbConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await PrepareAgeAsync(connection, cancellationToken).ConfigureAwait(false);

            foreach (var node in projection.Nodes)
            {
                await ExecuteCypherAsync(connection, BuildNodeCypher(node), cancellationToken).ConfigureAwait(false);
            }

            foreach (var edge in projection.Edges)
            {
                await ExecuteCypherAsync(connection, BuildEdgeCypher(edge), cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Ingested chat history graph projection with {NodeCount} node(s) and {EdgeCount} edge(s) for {DocId}.",
                projection.Nodes.Count,
                projection.Edges.Count,
                request.Document.DocId);

            return ServiceResponse<GraphIngestionResult>.Success(new GraphIngestionResult
            {
                Accepted = true,
                NodesWritten = projection.Nodes.Count,
                EdgesWritten = projection.Edges.Count
            });
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Chat history graph ingestion failed for {DocId}.", request.Document.DocId);
            return ServiceResponse<GraphIngestionResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Graph ingestion failed."),
                statusCode: 503,
                message: "Graph ingestion failed");
        }
    }

    private async Task PrepareAgeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.GraphQueryTimeoutSeconds;
        command.CommandText = $"""
            LOAD 'age';
            SET search_path = ag_catalog, "$user", public;
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM ag_catalog.ag_graph WHERE name = '{EscapeSqlLiteral(_options.GraphName)}') THEN
                PERFORM ag_catalog.create_graph('{EscapeSqlLiteral(_options.GraphName)}');
              END IF;
            END $$;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteCypherAsync(NpgsqlConnection connection, string query, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = _options.GraphQueryTimeoutSeconds;
        command.CommandText = $"""
            SELECT * FROM ag_catalog.cypher('{EscapeSqlLiteral(_options.GraphName)}', {DollarQuote(query, "cwi_graph_ingest")}) as (result agtype);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildNodeCypher(GraphIngestionNode node)
    {
        var labels = node.Labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(SanitizeLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var labelFragment = labels.Length == 0 ? ":node" : ":" + string.Join(':', labels);
        var properties = new Dictionary<string, object?>(node.Properties, StringComparer.OrdinalIgnoreCase)
        {
            ["node_id"] = node.NodeId
        };
        return $"MERGE (n{labelFragment} {{node_id: {CypherLiteral(node.NodeId)}}}) SET n += {CypherLiteral(properties)} RETURN n";
    }

    private static string BuildEdgeCypher(GraphIngestionEdge edge)
    {
        var properties = new Dictionary<string, object?>(edge.Properties, StringComparer.OrdinalIgnoreCase)
        {
            ["edge_id"] = edge.EdgeId
        };
        return
            $"MATCH (from {{node_id: {CypherLiteral(edge.FromNodeId)}}}), (to {{node_id: {CypherLiteral(edge.ToNodeId)}}}) " +
            $"MERGE (from)-[r:{SanitizeRelation(edge.Relation)} {{edge_id: {CypherLiteral(edge.EdgeId)}}}]->(to) " +
            $"SET r += {CypherLiteral(properties)} RETURN r";
    }

    private static string SanitizeLabel(string label)
    {
        var cleaned = Regex.Replace(label.Trim(), "[^A-Za-z0-9_]", "_");
        return string.IsNullOrWhiteSpace(cleaned) ? "node" : cleaned;
    }

    private static string SanitizeRelation(string relation)
    {
        var cleaned = Regex.Replace(relation.Trim(), "[^A-Za-z0-9_]", "_").ToUpperInvariant();
        return string.IsNullOrWhiteSpace(cleaned) ? "RELATED_TO" : cleaned;
    }

    private static string CypherLiteral(object? value)
    {
        return value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            int or long or short or byte or double or decimal or float => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
            DateTimeOffset date => CypherLiteral(date.UtcDateTime.ToString("O")),
            DateTime date => CypherLiteral(date.ToUniversalTime().ToString("O")),
            IReadOnlyDictionary<string, object?> map => CypherMapLiteral(map),
            IDictionary<string, object?> map => CypherMapLiteral(map),
            IEnumerable<string> items => "[" + string.Join(", ", items.Select(CypherLiteral)) + "]",
            _ => $"'{EscapeCypherString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)}'"
        };
    }

    private static string CypherMapLiteral(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        var first = true;
        foreach (var entry in values.Where(entry => entry.Value is not null).OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(SanitizePropertyKey(entry.Key));
            builder.Append(": ");
            builder.Append(CypherLiteral(entry.Value));
        }

        builder.Append('}');
        return builder.ToString();
    }

    private static string SanitizePropertyKey(string key)
    {
        var cleaned = Regex.Replace(key.Trim(), "[^A-Za-z0-9_]", "_");
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "value";
        }

        return char.IsDigit(cleaned[0]) ? $"_{cleaned}" : cleaned;
    }

    private static string EscapeCypherString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string DollarQuote(string value, string tag)
    {
        var token = tag;
        while (value.Contains($"${token}$", StringComparison.Ordinal))
        {
            token += "x";
        }

        return $"${token}$" + value + $"${token}$";
    }
}
#nullable disable
