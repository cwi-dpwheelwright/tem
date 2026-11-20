using Json.Schema;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.PackService.Domain.Validation;

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
            return ServiceResponse<bool>.Fail(new DomainError("invalid_pack", "pack.json is invalid JSON."), 400, "Invalid pack.");
        }

        var results = _schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Flag
        });

        return results.IsValid
            ? ServiceResponse<bool>.Success(true)
            : ServiceResponse<bool>.Fail(new DomainError("schema_invalid", "Pack schema validation failed."), 400, "Invalid pack.");
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(PackSchemaValidator).Assembly;
        const string resourceName = "Cwi.PackService.Domain.pack_schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded schema resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
