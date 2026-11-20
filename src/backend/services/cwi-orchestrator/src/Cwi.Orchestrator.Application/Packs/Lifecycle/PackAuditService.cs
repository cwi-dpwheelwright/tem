#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackAuditService : DomainServiceBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    private static readonly string[] PackIdKeys =
    [
        "pack_id",
        "packId",
        "PackId",
        "pack_name",
        "packName",
        "PackName"
    ];

    private static readonly string[] ActorKeys =
    [
        "actor_user_id",
        "actorUserId",
        "ActorUserId",
        "actor_id",
        "actorId",
        "ActivatedBy",
        "activated_by",
        "RolledBackBy",
        "rolled_back_by"
    ];

    private static readonly string[] TenantKeys =
    [
        "tenant_id",
        "tenantId",
        "TenantId"
    ];

    private readonly ILedgerRepository _ledgerRepository;

    public PackAuditService(ILedgerRepository ledgerRepository)
    {
        _ledgerRepository = ledgerRepository;
    }

    public Task<ServiceResponse<PackAuditResponseDto>> QueryAsync(
        PackAuditQuery request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<PackAuditResponseDto>> HandleAsync(
        PackAuditQuery request,
        CancellationToken cancellationToken)
    {
        if (request.StartUtc.HasValue && request.EndUtc.HasValue && request.StartUtc > request.EndUtc)
        {
            return ServiceResponse<PackAuditResponseDto>.Success(new PackAuditResponseDto
            {
                Total = 0,
                Limit = NormalizeLimit(request.Limit),
                Items = Array.Empty<PackAuditEventDto>()
            });
        }

        var normalizedEventType = Normalize(request.EventType);
        var normalizedPackId = Normalize(request.PackId);
        var normalizedActor = Normalize(request.ActorUserId);
        var normalizedTenant = Normalize(request.TenantId);
        var limit = NormalizeLimit(request.Limit);

        var searchEventType = ShouldUseExactEventType(normalizedEventType) ? normalizedEventType : null;

        var events = await _ledgerRepository
            .SearchAsync(request.StartUtc, request.EndUtc, searchEventType, cancellationToken)
            .ConfigureAwait(false);

        var filtered = new List<LedgerEvent>();
        foreach (var entry in events)
        {
            if (!MatchesEventType(entry, normalizedEventType))
            {
                continue;
            }

            if (normalizedPackId is null && normalizedActor is null && normalizedTenant is null)
            {
                filtered.Add(entry);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.PayloadJson))
            {
                continue;
            }

            if (!TryParsePayload(entry.PayloadJson, out var payload))
            {
                continue;
            }

            if (normalizedPackId is not null && !MatchesValue(payload, PackIdKeys, normalizedPackId))
            {
                continue;
            }

            if (normalizedActor is not null && !MatchesValue(payload, ActorKeys, normalizedActor))
            {
                continue;
            }

            if (normalizedTenant is not null && !MatchesValue(payload, TenantKeys, normalizedTenant))
            {
                continue;
            }

            filtered.Add(entry);
        }

        var ordered = filtered
            .OrderByDescending(entry => entry.CreatedUtc)
            .ToList();

        var total = ordered.Count;
        var items = ordered
            .Take(limit)
            .Select(MapEvent)
            .ToList();

        return ServiceResponse<PackAuditResponseDto>.Success(new PackAuditResponseDto
        {
            Total = total,
            Limit = limit,
            Items = items
        });
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaxLimit);
    }

    private static bool ShouldUseExactEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var normalized = eventType.Trim();
        return !normalized.EndsWith('*') && !string.Equals(normalized, "pack", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalized, "pack.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEventType(LedgerEvent entry, string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return entry.EventType.StartsWith("pack.", StringComparison.OrdinalIgnoreCase);
        }

        var normalized = eventType.Trim();
        if (normalized.EndsWith('*'))
        {
            var prefix = normalized[..^1];
            return entry.EventType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalized, "pack", StringComparison.OrdinalIgnoreCase))
        {
            return entry.EventType.StartsWith("pack.", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalized, "pack.", StringComparison.OrdinalIgnoreCase))
        {
            return entry.EventType.StartsWith("pack.", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry.EventType, normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePayload(string payloadJson, out JsonElement payload)
    {
        payload = default;
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            payload = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool MatchesValue(JsonElement payload, string[] keys, string expected)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetProperty(key, out var property))
            {
                continue;
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value) &&
                string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static PackAuditEventDto MapEvent(LedgerEvent entry)
    {
        return new PackAuditEventDto
        {
            TraceId = entry.TraceId,
            EventType = entry.EventType,
            PayloadJson = entry.PayloadJson,
            Hash = entry.Hash,
            CreatedUtc = entry.CreatedUtc
        };
    }
}
#nullable disable
