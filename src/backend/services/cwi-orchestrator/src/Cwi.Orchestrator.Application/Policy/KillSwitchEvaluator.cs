using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Policy;

public sealed class KillSwitchEvaluator
{
    private readonly ITenantConfigRepository _tenantConfigRepository;

    public KillSwitchEvaluator(ITenantConfigRepository tenantConfigRepository)
    {
        _tenantConfigRepository = tenantConfigRepository;
    }

    public async Task<ServiceResponse<bool>> IsActionDisabledAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<bool>.Success(false);
        }

        var config = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (config is null || string.IsNullOrWhiteSpace(config.ConfigJson))
        {
            return ServiceResponse<bool>.Success(false);
        }

        try
        {
            using var document = JsonDocument.Parse(config.ConfigJson);
            var disabled = ResolveActionKillSwitch(document.RootElement);
            return ServiceResponse<bool>.Success(disabled);
        }
        catch (JsonException)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InternalError, "Tenant kill switch invalid."),
                statusCode: 500,
                message: "Tenant kill switch invalid");
        }
    }

    private static bool ResolveActionKillSwitch(JsonElement root)
    {
        if (TryGetBool(root, out var rootBool))
        {
            return rootBool;
        }

        if (TryGetBool(root, "kill_switch", out var killSwitch))
        {
            return killSwitch;
        }

        if (TryGetBool(root, "kill_switch_action", out var actionSwitch))
        {
            return actionSwitch;
        }

        if (TryGetBool(root, "action_kill_switch", out var actionSwitchAlt))
        {
            return actionSwitchAlt;
        }

        if (TryGetBool(root, "kill_switch", "action", out var actionNested))
        {
            return actionNested;
        }

        if (TryGetBool(root, "kill_switch", "enabled", out var enabled))
        {
            return enabled;
        }

        if (TryGetBool(root, "kill_switches", "action", out var actionNestedAlt))
        {
            return actionNestedAlt;
        }

        return false;
    }

    private static bool TryGetBool(JsonElement root, out bool value)
    {
        value = false;
        if (root.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (root.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryGetBool(JsonElement root, string property, out bool value)
    {
        value = false;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(property, out var child))
        {
            return false;
        }

        return TryGetBool(child, out value);
    }

    private static bool TryGetBool(JsonElement root, string parent, string child, out bool value)
    {
        value = false;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(parent, out var parentElement) ||
            parentElement.ValueKind != JsonValueKind.Object ||
            !parentElement.TryGetProperty(child, out var childElement))
        {
            return false;
        }

        return TryGetBool(childElement, out value);
    }
}
