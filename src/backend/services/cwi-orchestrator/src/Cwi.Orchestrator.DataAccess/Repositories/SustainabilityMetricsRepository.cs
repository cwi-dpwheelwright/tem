#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class SustainabilityMetricsRepository : ISustainabilityMetricsRepository
{
    private readonly OrchestratorDbContext _db;

    public SustainabilityMetricsRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<SustainabilityRequestRecord?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        return _db.SustainabilityRequestRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.RequestId == requestId, cancellationToken);
    }

    public async Task AddAsync(
        SustainabilityRequestRecord record,
        CancellationToken cancellationToken)
    {
        _db.SustainabilityRequestRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(
        SustainabilityRequestRecord record,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SustainabilityRequestRecords
            .FirstOrDefaultAsync(item => item.RequestId == record.RequestId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.SustainabilityRequestRecords.Add(record);
        }
        else
        {
            CopyValues(record, existing);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SustainabilityRequestRecord>> QueryAsync(
        string? tenantId,
        string? userIdHash,
        string? nodeId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.SustainabilityRequestRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(record => record.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(userIdHash))
        {
            query = query.Where(record => record.UserIdHash == userIdHash);
        }

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(record => record.NodeId == nodeId);
        }

        if (startUtc.HasValue)
        {
            query = query.Where(record => record.TimestampUtc >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            query = query.Where(record => record.TimestampUtc <= endUtc.Value);
        }

        return await query
            .OrderByDescending(record => record.TimestampUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void CopyValues(
        SustainabilityRequestRecord source,
        SustainabilityRequestRecord target)
    {
        target.TraceId = source.TraceId;
        target.TimestampUtc = source.TimestampUtc;
        target.TenantId = source.TenantId;
        target.UserIdHash = source.UserIdHash;
        target.UserRole = source.UserRole;
        target.NodeId = source.NodeId;
        target.NodeName = source.NodeName;
        target.NodeGroup = source.NodeGroup;
        target.NodeRole = source.NodeRole;
        target.DurationSeconds = source.DurationSeconds;
        target.EnergyWh = source.EnergyWh;
        target.EnergyValueType = source.EnergyValueType;
        target.CostUsd = source.CostUsd;
        target.CostValueType = source.CostValueType;
        target.WaterLiters = source.WaterLiters;
        target.WaterValueType = source.WaterValueType;
        target.BaselineModelVersion = source.BaselineModelVersion;
        target.CloudBaselineEnergyWh = source.CloudBaselineEnergyWh;
        target.CloudBaselineEnergyValueType = source.CloudBaselineEnergyValueType;
        target.CloudBaselineCostUsd = source.CloudBaselineCostUsd;
        target.CloudBaselineCostValueType = source.CloudBaselineCostValueType;
        target.CloudBaselineWaterLiters = source.CloudBaselineWaterLiters;
        target.CloudBaselineWaterValueType = source.CloudBaselineWaterValueType;
        target.EnergySavedWh = source.EnergySavedWh;
        target.CostSavedUsd = source.CostSavedUsd;
        target.WaterSavedLiters = source.WaterSavedLiters;
        target.SavingsValueType = source.SavingsValueType;
        target.Status = source.Status;
        target.ModelOrRuntime = source.ModelOrRuntime;
        target.Source = source.Source;
        target.CreatedUtc = source.CreatedUtc;
        target.NodeWatts = source.NodeWatts;
        target.WattsSource = source.WattsSource;
        target.PrometheusInstance = source.PrometheusInstance;
        target.TelemetrySource = source.TelemetrySource;
        target.CalculationVersion = source.CalculationVersion;
    }
}
#nullable disable
