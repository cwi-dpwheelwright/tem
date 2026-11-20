#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed class SuccessionPlanStore
{
    private readonly ConcurrentDictionary<string, List<SuccessionPlanRecord>> _plansByTenant =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SuccessionPlanRecord> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public SuccessionPlanRecord Add(SuccessionPlanRequestDto request)
    {
        var record = new SuccessionPlanRecord
        {
            PlanId = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId.Trim(),
            AssetId = request.AssetId.Trim(),
            CurrentOwnerId = request.CurrentOwnerId.Trim(),
            EffectiveUtc = request.EffectiveUtc,
            Successors = request.Successors
                .OrderBy(s => s.Order)
                .Select(s => new SuccessionSuccessorDto
                {
                    OwnerId = s.OwnerId.Trim(),
                    Order = s.Order,
                    Trigger = s.Trigger.Trim(),
                    ApprovalRequired = s.ApprovalRequired
                })
                .ToList(),
            Status = "active",
            CreatedUtc = DateTimeOffset.UtcNow
        };

        lock (_gate)
        {
            var list = _plansByTenant.GetOrAdd(record.TenantId, _ => new List<SuccessionPlanRecord>());
            list.Add(record);
        }

        return record;
    }

    public SuccessionPlanRecord? Get(string tenantId, string planId)
    {
        if (_plansByTenant.TryGetValue(tenantId, out var list))
        {
            return list.FirstOrDefault(plan => plan.PlanId == planId);
        }

        return null;
    }

    public string CreateSnapshot(SuccessionPlanRecord record)
    {
        var snapshotId = $"snap-{record.PlanId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        _snapshots[snapshotId] = Clone(record);
        return snapshotId;
    }

    public SuccessionPlanRecord? RestoreSnapshot(string tenantId, string snapshotId)
    {
        if (!_snapshots.TryGetValue(snapshotId, out var record))
        {
            return null;
        }

        lock (_gate)
        {
            var list = _plansByTenant.GetOrAdd(tenantId, _ => new List<SuccessionPlanRecord>());
            if (list.Any(plan => plan.PlanId == record.PlanId))
            {
                return record;
            }

            list.Add(record);
            return record;
        }
    }

    private static SuccessionPlanRecord Clone(SuccessionPlanRecord record)
    {
        return new SuccessionPlanRecord
        {
            PlanId = record.PlanId,
            TenantId = record.TenantId,
            AssetId = record.AssetId,
            CurrentOwnerId = record.CurrentOwnerId,
            Status = record.Status,
            EffectiveUtc = record.EffectiveUtc,
            Successors = record.Successors
                .Select(s => new SuccessionSuccessorDto
                {
                    OwnerId = s.OwnerId,
                    Order = s.Order,
                    Trigger = s.Trigger,
                    ApprovalRequired = s.ApprovalRequired
                })
                .ToList(),
            CreatedUtc = record.CreatedUtc
        };
    }

    public sealed class SuccessionPlanRecord
    {
        public string PlanId { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string AssetId { get; init; } = string.Empty;
        public string CurrentOwnerId { get; init; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? EffectiveUtc { get; init; }
        public List<SuccessionSuccessorDto> Successors { get; init; } = new();
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
#nullable disable
