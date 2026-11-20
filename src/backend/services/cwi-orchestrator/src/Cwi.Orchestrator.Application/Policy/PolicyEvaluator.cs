#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Policy;

public interface IPolicyEvaluator
{
    ServiceResponse<PolicyDecision> Evaluate(
        string policyMode,
        string actionType,
        IReadOnlyDictionary<string, object?> payload);
}

public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public ServiceResponse<PolicyDecision> Evaluate(
        string policyMode,
        string actionType,
        IReadOnlyDictionary<string, object?> payload)
    {
        if (!PolicyModeParser.TryParse(policyMode, out var mode))
        {
            return ServiceResponse<PolicyDecision>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Unknown policy_mode."),
                statusCode: 400,
                message: "Invalid policy_mode");
        }

        var risk = ClassifyRisk(actionType, payload);
        var decision = mode switch
        {
            PolicyMode.Approve => new PolicyDecision(mode, risk, RequiresApproval: true, AllowExecution: false, Reason: "Approval required by policy mode."),
            PolicyMode.AutoWithinBounds when risk == RiskLevel.High => new PolicyDecision(mode, risk, RequiresApproval: true, AllowExecution: false, Reason: "High-risk action requires approval."),
            PolicyMode.AutoWithinBounds => new PolicyDecision(mode, risk, RequiresApproval: false, AllowExecution: true, Reason: "Auto-approved within risk bounds."),
            PolicyMode.Recommend => new PolicyDecision(mode, risk, RequiresApproval: false, AllowExecution: true, Reason: "Executed with recommendation review."),
            _ => new PolicyDecision(mode, risk, RequiresApproval: false, AllowExecution: true, Reason: "Executed in observe mode.")
        };

        return ServiceResponse<PolicyDecision>.Success(decision);
    }

    private static RiskLevel ClassifyRisk(
        string actionType,
        IReadOnlyDictionary<string, object?> payload)
    {
        if (TryGetRiskOverride(payload, out var overrideRisk))
        {
            return overrideRisk;
        }

        if (TryGetBool(payload, "destructive") || TryGetBool(payload, "delete") ||
            TryGetBool(payload, "purge") || TryGetBool(payload, "wipe"))
        {
            return RiskLevel.High;
        }

        if (TryGetNumeric(payload, out var amount))
        {
            if (amount >= 1000)
            {
                return RiskLevel.High;
            }

            if (amount >= 100)
            {
                return RiskLevel.Medium;
            }
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            return RiskLevel.Medium;
        }

        var normalized = actionType.Trim().ToLowerInvariant();
        if (normalized.Contains("delete") || normalized.Contains("transfer") || normalized.Contains("payment") ||
            normalized.Contains("wire") || normalized.Contains("deploy") || normalized.Contains("purge"))
        {
            return RiskLevel.High;
        }

        if (normalized.Contains("update") || normalized.Contains("modify") || normalized.Contains("write") ||
            normalized.Contains("create"))
        {
            return RiskLevel.Medium;
        }

        return RiskLevel.Low;
    }

    private static bool TryGetRiskOverride(
        IReadOnlyDictionary<string, object?> payload,
        out RiskLevel risk)
    {
        risk = RiskLevel.Medium;

        if (TryGetString(payload, "risk_level", out var value) ||
            TryGetString(payload, "risk", out value))
        {
            if (TryParseRisk(value, out var parsed))
            {
                risk = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseRisk(string? value, out RiskLevel risk)
    {
        risk = RiskLevel.Medium;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "high" => (risk = RiskLevel.High) == RiskLevel.High,
            "medium" => (risk = RiskLevel.Medium) == RiskLevel.Medium,
            "low" => (risk = RiskLevel.Low) == RiskLevel.Low,
            _ => false
        };
    }

    private static bool TryGetBool(
        IReadOnlyDictionary<string, object?> payload,
        string key)
    {
        if (!payload.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
                _ => false
            };
        }

        if (value is string stringValue)
        {
            return bool.TryParse(stringValue, out var parsed) && parsed;
        }

        return false;
    }

    private static bool TryGetNumeric(
        IReadOnlyDictionary<string, object?> payload,
        out double value)
    {
        value = 0;
        if (!TryGetDouble(payload, "amount", out value) &&
            !TryGetDouble(payload, "value", out value))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetDouble(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        out double value)
    {
        value = 0;
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return double.TryParse(element.GetString(), out value);
            case string text:
                return double.TryParse(text, out value);
            default:
                return false;
        }
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        out string? value)
    {
        value = null;
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is string text)
        {
            value = text;
            return true;
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        return false;
    }
}
#nullable disable
