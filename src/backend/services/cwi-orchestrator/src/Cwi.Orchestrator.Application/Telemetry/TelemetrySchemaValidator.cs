using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Json.Schema;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetrySchemaValidator
{
    private readonly JsonSchema _schema;

    public TelemetrySchemaValidator()
    {
        _schema = JsonSchema.FromText(LoadSchemaText());
    }

    public ServiceResponse<bool> Validate(TelemetryIngestRequestDto request)
    {
        var node = JsonSerializer.SerializeToNode(request);
        if (node is null)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Telemetry request payload missing."),
                statusCode: 400,
                message: "Telemetry request payload missing");
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
            new DomainError(ErrorCodes.InvalidRequest, "Telemetry request failed schema validation."),
            statusCode: 400,
            message: "Telemetry request failed schema validation");
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(TelemetrySchemaValidator).Assembly;
        const string resourceName = "Cwi.Orchestrator.Application.Telemetry.telemetry_schema.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded schema resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
