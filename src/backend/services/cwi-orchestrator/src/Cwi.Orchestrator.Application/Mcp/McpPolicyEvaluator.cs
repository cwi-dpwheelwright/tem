#nullable enable
namespace Cwi.Orchestrator.Application.Mcp;

public sealed class McpPolicyEvaluator
{
    public McpPolicyEvaluation Evaluate(
        string actionType,
        McpToolDefinition? tool,
        McpPolicyDocument? policy,
        McpExecutionOptions? options = null,
        double? intentConfidence = null)
    {
        if (policy?.ExtensionData?.TryGetValue("global_tool_execution_enabled", out var globalEnabled) == true
            && globalEnabled.ValueKind == System.Text.Json.JsonValueKind.False)
        {
            return new McpPolicyEvaluation(true, false, true, "Global MCP tool execution is disabled by policy.");
        }

        if (options is not null && options.IsDisabled)
        {
            return new McpPolicyEvaluation(true, false, true, "MCP execution is disabled by runtime mode.");
        }

        if (intentConfidence.HasValue && intentConfidence.Value < 0.70d)
        {
            return new McpPolicyEvaluation(
                true,
                false,
                true,
                intentConfidence.Value >= 0.50d
                    ? "Intent confidence is below the execution threshold; clarification is required before tool execution."
                    : "Intent confidence is too low for tool execution.");
        }

        if (tool is null)
        {
            var blockUnregistered = policy?.Rules.Any(rule =>
                rule.Enforce &&
                string.Equals(rule.Id, "block-unauthorized-tools", StringComparison.OrdinalIgnoreCase)) != false;

            return blockUnregistered
                ? new McpPolicyEvaluation(true, false, true, $"Tool '{actionType}' is not registered in the MCP registry.")
                : new McpPolicyEvaluation(false, false, true, "Unregistered tool allowed by policy.");
        }

        var requiresLedger = tool.LogToLedger;
        var requiresApproval = tool.RequiresApproval;
        var reasons = new List<string>();

        if (IsToolDisabledByPolicy(tool.Name, policy))
        {
            return new McpPolicyEvaluation(true, false, true, $"Tool '{tool.Name}' is disabled by MCP policy.");
        }

        if (options is not null && options.IsReadOnly)
        {
            if (!IsReadOnlyToolAllowed(tool.Name, options, policy))
            {
                return new McpPolicyEvaluation(true, false, true, $"Tool '{tool.Name}' is not approved for Day-1 read-only execution.");
            }

            if (tool.RequiresApproval || !string.Equals(tool.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                return new McpPolicyEvaluation(true, false, true, $"Tool '{tool.Name}' is not read-only and cannot execute in Day-1 read-only mode.");
            }
        }

        if (tool.RequiresApproval)
        {
            reasons.Add("Tool requires approval.");
        }

        if (policy is not null)
        {
            foreach (var rule in policy.Rules.Where(rule => rule.Enforce))
            {
                if (string.Equals(rule.Id, "require-ledger-logging", StringComparison.OrdinalIgnoreCase))
                {
                    if (!tool.LogToLedger)
                    {
                        return new McpPolicyEvaluation(true, false, true, $"Tool '{tool.Name}' is blocked because ledger logging is required.");
                    }

                    requiresLedger = true;
                }

                if (string.Equals(rule.Id, "category-based-approval", StringComparison.OrdinalIgnoreCase) &&
                    rule.Categories is not null &&
                    rule.Categories.TryGetValue(tool.Category, out var categoryRequiresApproval) &&
                    categoryRequiresApproval)
                {
                    requiresApproval = true;
                    reasons.Add($"Category '{tool.Category}' requires approval.");
                }

                if (string.Equals(rule.Id, "explicit-sensitive-tools", StringComparison.OrdinalIgnoreCase) &&
                    rule.Tools?.Any(name => string.Equals(name, tool.Name, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    requiresApproval = true;
                    reasons.Add($"Tool '{tool.Name}' is explicitly sensitive.");
                }
            }
        }

        return new McpPolicyEvaluation(false, requiresApproval, requiresLedger, reasons.Count == 0
            ? "Tool allowed by MCP policy."
            : string.Join(" ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static bool IsReadOnlyToolAllowed(string toolName, McpExecutionOptions options, McpPolicyDocument? policy)
    {
        if (TryReadStringArray(policy?.ExtensionData, "read_only_allowed_tools", out var policyAllowedTools))
        {
            return policyAllowedTools.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase));
        }

        return options.IsReadOnlyToolAllowed(toolName);
    }

    private static bool IsToolDisabledByPolicy(string toolName, McpPolicyDocument? policy)
    {
        if (policy is null)
        {
            return false;
        }

        if (TryReadStringArray(policy.ExtensionData, "disabled_tools", out var documentDisabledTools)
            && documentDisabledTools.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var rule in policy.Rules.Where(rule => rule.Enforce))
        {
            if ((string.Equals(rule.Id, "disabled-tools", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.Id, "phase1-disabled-tools", StringComparison.OrdinalIgnoreCase))
                && rule.Tools?.Any(name => string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase)) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadStringArray(
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? extensionData,
        string key,
        out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        if (extensionData is null
            || !extensionData.TryGetValue(key, out var element)
            || element.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        values = element
            .EnumerateArray()
            .Where(item => item.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        return values.Count > 0;
    }
}
#nullable disable
