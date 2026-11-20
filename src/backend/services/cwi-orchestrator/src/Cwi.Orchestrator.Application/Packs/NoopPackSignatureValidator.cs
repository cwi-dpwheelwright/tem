using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class NoopPackSignatureValidator : IPackSignatureValidator
{
    public ServiceResponse<bool> Validate(PackDocument document)
    {
        using var json = JsonDocument.Parse(document.Json);
        if (!json.RootElement.TryGetProperty("signature", out var signature))
        {
            return ServiceResponse<bool>.Success(true);
        }

        var signatureValue = signature.ValueKind switch
        {
            JsonValueKind.String => signature.GetString(),
            JsonValueKind.Object => signature.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.String
                ? hash.GetString()
                : signature.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(signatureValue))
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack signature invalid."),
                statusCode: 400,
                message: "Pack signature invalid");
        }

        if (!string.Equals(signatureValue.Trim(), document.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack signature mismatch."),
                statusCode: 400,
                message: "Pack signature mismatch");
        }

        return ServiceResponse<bool>.Success(true);
    }
}
