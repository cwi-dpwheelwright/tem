#nullable enable
using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed class OwnershipLineageBindingStore
{
    private readonly ConcurrentDictionary<string, List<BindingRecord>> _bindingsByTenant =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public BindingRecord Add(
        string tenantId,
        string assetId,
        string ownerId,
        string relationship,
        string? parentAssetId,
        string? reason)
    {
        var record = new BindingRecord
        {
            BindingId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            AssetId = assetId,
            OwnerId = ownerId,
            Relationship = relationship,
            ParentAssetId = parentAssetId,
            Reason = reason,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            var list = _bindingsByTenant.GetOrAdd(tenantId, _ => new List<BindingRecord>());
            list.Add(record);
        }

        return record;
    }

    public IReadOnlyList<BindingRecord> List(string tenantId)
    {
        if (_bindingsByTenant.TryGetValue(tenantId, out var list))
        {
            return list.ToList();
        }

        return Array.Empty<BindingRecord>();
    }

    public sealed class BindingRecord
    {
        public string BindingId { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string AssetId { get; init; } = string.Empty;
        public string OwnerId { get; init; } = string.Empty;
        public string Relationship { get; init; } = string.Empty;
        public string? ParentAssetId { get; init; }
        public string? Reason { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
#nullable disable
