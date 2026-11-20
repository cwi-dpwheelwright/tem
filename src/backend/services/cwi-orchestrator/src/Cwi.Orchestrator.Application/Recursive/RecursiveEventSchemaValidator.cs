#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Json.Schema;

namespace Cwi.Orchestrator.Application.Recursive;

public sealed class RecursiveEventSchemaValidator
{
    private readonly JsonSchema _schema;

    public RecursiveEventSchemaValidator()
    {
        _schema = JsonSchema.FromText(LoadSchemaText());
    }

    public ServiceResponse<bool> ValidateJson(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return Fail("Recursive event payload missing.");
        }

        try
        {
            var node = JsonNode.Parse(payloadJson);
            return Validate(node);
        }
        catch (JsonException)
        {
            return Fail("Recursive event payload is not valid JSON.");
        }
    }

    public ServiceResponse<bool> Validate(JsonNode? node)
    {
        if (node is null)
        {
            return Fail("Recursive event payload missing.");
        }

        var results = _schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Flag
        });

        return results.IsValid
            ? ServiceResponse<bool>.Success(true)
            : Fail("Recursive event payload failed schema validation.");
    }

    private static ServiceResponse<bool> Fail(string message)
    {
        return ServiceResponse<bool>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode: 400,
            message: message);
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(RecursiveEventSchemaValidator).Assembly;
        const string resourceName = "Cwi.Orchestrator.Application.Recursive.recursive_event_schema.json";
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
