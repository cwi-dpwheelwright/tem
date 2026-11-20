#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Json.Schema;

namespace Cwi.Orchestrator.Application.Packs.Ingest;

public sealed class PackSchemaValidator
{
    private readonly JsonSchema _schema;

    public PackSchemaValidator()
    {
        _schema = JsonSchema.FromText(LoadSchemaText());
    }

    public ServiceResponse<bool> Validate(JsonNode? node)
    {
        if (node is null)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack payload missing."),
                statusCode: 400,
                message: "Pack payload missing");
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
            new DomainError(ErrorCodes.InvalidRequest, "Pack schema validation failed."),
            statusCode: 400,
            message: "Pack schema validation failed");
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(PackSchemaValidator).Assembly;
        const string resourceName = "Cwi.Orchestrator.Application.Packs.pack_schema.json";
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
