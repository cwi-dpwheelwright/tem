#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Mcp;

public sealed class McpActionLedgerRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _factory;

    public McpActionLedgerRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory factory)
    {
        _ledgerRepository = ledgerRepository;
        _factory = factory;
    }

    public async Task RecordAsync(
        string traceId,
        string tenantId,
        string userId,
        string actionType,
        McpToolDefinition? tool,
        McpPolicyEvaluation? policy,
        bool executed,
        string status,
        IReadOnlyDictionary<string, object?>? result,
        CancellationToken cancellationToken)
    {
        var previousHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event"] = "MCP_ACTION",
            ["tenant_id"] = tenantId,
            ["user_id"] = userId,
            ["action_type"] = actionType,
            ["tool"] = tool is null ? null : new Dictionary<string, object?>
            {
                ["name"] = tool.Name,
                ["category"] = tool.Category,
                ["method"] = tool.Method,
                ["endpoint"] = tool.Endpoint,
                ["requires_approval"] = tool.RequiresApproval,
                ["log_to_ledger"] = tool.LogToLedger
            },
            ["policy"] = policy is null ? null : new Dictionary<string, object?>
            {
                ["blocked"] = policy.Blocked,
                ["requires_approval"] = policy.RequiresApproval,
                ["requires_ledger"] = policy.RequiresLedger,
                ["reason"] = policy.Reason
            },
            ["executed"] = executed,
            ["status"] = status,
            ["result"] = result
        });

        var ledgerEvent = _factory.CreateSignedEvent(traceId, "mcp.action", payload, previousHash);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
