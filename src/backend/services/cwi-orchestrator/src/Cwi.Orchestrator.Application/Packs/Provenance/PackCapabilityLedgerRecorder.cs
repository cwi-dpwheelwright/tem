using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Packs.Provenance;

public interface IPackCapabilityLedgerRecorder
{
    Task RecordAsync(
        string traceId,
        string packName,
        string packHash,
        string packVersion,
        string policyMode,
        string? modelId,
        string? policyVersion,
        IReadOnlyList<PackCapabilitySummary> capabilities,
        PackCapabilityDecision decision,
        CancellationToken cancellationToken);
}

public sealed class PackCapabilityLedgerRecorder : IPackCapabilityLedgerRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public PackCapabilityLedgerRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task RecordAsync(
        string traceId,
        string packName,
        string packHash,
        string packVersion,
        string policyMode,
        string? modelId,
        string? policyVersion,
        IReadOnlyList<PackCapabilitySummary> capabilities,
        PackCapabilityDecision decision,
        CancellationToken cancellationToken)
    {
        var payload = new PackCapabilityLedgerPayload
        {
            TraceId = traceId,
            PackName = packName,
            PackHash = packHash,
            PackVersion = packVersion,
            PolicyMode = policyMode,
            ModelId = modelId,
            PolicyVersion = policyVersion,
            Capabilities = capabilities,
            BlockingCapabilities = decision.BlockingCapabilities,
            DecisionReason = decision.Reason,
            RequiresApproval = decision.RequiresApproval,
            AllowExecution = decision.AllowExecution,
            Risk = decision.Risk.ToString().ToLowerInvariant(),
            EvaluatedUtc = decision.EvaluatedUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "pack.capability.enforcement",
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
