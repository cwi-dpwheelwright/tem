#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Json.Schema;

namespace Cwi.Orchestrator.Application.Etl;

public sealed class IngestDocumentSchemaValidator
{
    private readonly JsonSchema _schema;

    public IngestDocumentSchemaValidator()
    {
        _schema = JsonSchema.FromText(LoadSchemaText());
    }

    public ServiceResponse<bool> Validate(IngestDocumentRequestDto request)
    {
        var node = JsonSerializer.SerializeToNode(request);
        if (node is null)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Ingest document payload missing."),
                statusCode: 400,
                message: "Ingest document payload missing");
        }

        var results = _schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Flag
        });

        if (results.IsValid)
        {
            return ServiceResponse<bool>.Success(true);
        }

        return ServiceResponse<bool>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, "Ingest document payload failed schema validation."),
            statusCode: 400,
            message: "Ingest document payload failed schema validation");
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(IngestDocumentSchemaValidator).Assembly;
        const string resourceName = "Cwi.Orchestrator.Application.Etl.ingest_document_schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded schema resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
#nullable disable
