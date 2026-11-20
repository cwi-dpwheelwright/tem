using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Policy;

public sealed class PolicyDefaultsResolver
{
    private readonly ITenantConfigRepository _tenantConfigRepository;

    public PolicyDefaultsResolver(ITenantConfigRepository tenantConfigRepository)
    {
        _tenantConfigRepository = tenantConfigRepository;
    }

    public async Task<ServiceResponse<string>> ResolveAsync(
        string tenantId,
        string requestedPolicyMode,
        PackDocument? packDocument,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedPolicyMode))
        {
            return ServiceResponse<string>.Success(requestedPolicyMode);
        }

        var tenantOverride = await ResolveTenantOverrideAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!tenantOverride.IsSuccess)
        {
            return ServiceResponse<string>.Fail(
                tenantOverride.Error ?? new DomainError(ErrorCodes.InternalError, "Tenant policy override failed."),
                statusCode: tenantOverride.StatusCode,
                message: tenantOverride.Message);
        }

        if (!string.IsNullOrWhiteSpace(tenantOverride.Data))
        {
            return ServiceResponse<string>.Success(tenantOverride.Data!);
        }

        var packDefault = ResolvePackDefault(packDocument);
        if (!packDefault.IsSuccess)
        {
            return ServiceResponse<string>.Fail(
                packDefault.Error ?? new DomainError(ErrorCodes.InternalError, "Pack policy defaults failed."),
                statusCode: packDefault.StatusCode,
                message: packDefault.Message);
        }

        if (!string.IsNullOrWhiteSpace(packDefault.Data))
        {
            return ServiceResponse<string>.Success(packDefault.Data!);
        }

        return ServiceResponse<string>.Success("observe");
    }

    private async Task<ServiceResponse<string>> ResolveTenantOverrideAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<string>.Success(string.Empty);
        }

        var config = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (config is null || string.IsNullOrWhiteSpace(config.ConfigJson))
        {
            return ServiceResponse<string>.Success(string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(config.ConfigJson);
            var mode = ExtractTenantPolicyMode(document.RootElement);
            return ServiceResponse<string>.Success(mode ?? string.Empty);
        }
        catch (JsonException)
        {
            return ServiceResponse<string>.Fail(
                new DomainError(ErrorCodes.InternalError, "Tenant policy overrides invalid."),
                statusCode: 500,
                message: "Tenant policy overrides invalid");
        }
    }

    private static ServiceResponse<string> ResolvePackDefault(PackDocument? packDocument)
    {
        if (packDocument is null || string.IsNullOrWhiteSpace(packDocument.Json))
        {
            return ServiceResponse<string>.Success(string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(packDocument.Json);
            var mode = ExtractPackPolicyMode(document.RootElement);
            return ServiceResponse<string>.Success(mode ?? string.Empty);
        }
        catch (JsonException)
        {
            return ServiceResponse<string>.Fail(
                new DomainError(ErrorCodes.InternalError, "Pack policy defaults invalid."),
                statusCode: 500,
                message: "Pack policy defaults invalid");
        }
    }

    private static string? ExtractTenantPolicyMode(JsonElement root)
    {
        return TryGetString(root, "policy_overrides", "policy_mode")
            ?? TryGetString(root, "policy_overrides", "mode")
            ?? TryGetString(root, "policy_mode")
            ?? TryGetString(root, "mode");
    }

    private static string? ExtractPackPolicyMode(JsonElement root)
    {
        return TryGetString(root, "policy_defaults", "policy_mode")
            ?? TryGetString(root, "policy_defaults", "mode");
    }

    private static string? TryGetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()
            : null;
    }
}
