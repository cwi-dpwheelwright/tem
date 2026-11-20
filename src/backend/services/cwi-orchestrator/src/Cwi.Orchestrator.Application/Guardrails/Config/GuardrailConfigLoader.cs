using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Json.Schema;

namespace Cwi.Orchestrator.Application.Guardrails.Config;

public sealed class GuardrailConfigLoader
{
    private readonly JsonSchema _schema;

    public GuardrailConfigLoader()
    {
        _schema = JsonSchema.FromText(LoadSchemaText());
    }

    public ServiceResponse<GuardrailConfig> Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ServiceResponse<GuardrailConfig>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Guardrail config payload missing."),
                statusCode: 400,
                message: "Guardrail config payload missing.");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return ServiceResponse<GuardrailConfig>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Guardrail config JSON invalid."),
                statusCode: 400,
                message: "Guardrail config JSON invalid.");
        }

        if (node is null)
        {
            return ServiceResponse<GuardrailConfig>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Guardrail config JSON invalid."),
                statusCode: 400,
                message: "Guardrail config JSON invalid.");
        }

        var results = _schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.Flag
        });

        if (!results.IsValid)
        {
            return ServiceResponse<GuardrailConfig>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Guardrail config failed schema validation."),
                statusCode: 400,
                message: "Guardrail config failed schema validation.");
        }

        GuardrailConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<GuardrailConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException)
        {
            config = null;
        }

        if (config is null)
        {
            return ServiceResponse<GuardrailConfig>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Guardrail config JSON invalid."),
                statusCode: 400,
                message: "Guardrail config JSON invalid.");
        }

        return ServiceResponse<GuardrailConfig>.Success(config);
    }

    private static string LoadSchemaText()
    {
        var assembly = typeof(GuardrailConfigLoader).Assembly;
        const string resourceName = "Cwi.Orchestrator.Application.Guardrails.guardrails_constraints.json";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded schema resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
