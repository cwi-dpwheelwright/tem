#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerEventQueryService
{
    private const int MaxPageSize = 200;
    private readonly ILedgerRepository _ledgerRepository;

    public LedgerEventQueryService(ILedgerRepository ledgerRepository)
    {
        _ledgerRepository = ledgerRepository;
    }

    public async Task<LedgerEventPage> QueryAsync(
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        string? tenantId,
        string? eventType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedSize = pageSize < 1 ? 1 : Math.Min(pageSize, MaxPageSize);
        var normalizedEventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();
        var normalizedTenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim();

        if (startUtc.HasValue && endUtc.HasValue && startUtc.Value > endUtc.Value)
        {
            return new LedgerEventPage
            {
                Page = normalizedPage,
                PageSize = normalizedSize,
                Total = 0,
                Items = Array.Empty<LedgerEvent>()
            };
        }

        var events = await _ledgerRepository
            .SearchAsync(startUtc, endUtc, normalizedEventType, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(normalizedTenantId))
        {
            events = events
                .Where(entry => MatchesTenant(entry.PayloadJson, normalizedTenantId))
                .ToList();
        }

        var total = events.Count;
        var items = events
            .Skip((normalizedPage - 1) * normalizedSize)
            .Take(normalizedSize)
            .ToList();

        return new LedgerEventPage
        {
            Page = normalizedPage,
            PageSize = normalizedSize,
            Total = total,
            Items = items
        };
    }

    private static bool MatchesTenant(string payloadJson, string tenantId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryGetTenant(document.RootElement, "tenant_id", tenantId)
                || TryGetTenant(document.RootElement, "tenantId", tenantId)
                || TryGetTenant(document.RootElement, "TenantId", tenantId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetTenant(JsonElement element, string propertyName, string tenantId)
    {
        if (!element.TryGetProperty(propertyName, out var tenantProperty))
        {
            return false;
        }

        var value = tenantProperty.ValueKind == JsonValueKind.String ? tenantProperty.GetString() : null;
        return !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, tenantId, StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
