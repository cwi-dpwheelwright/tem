#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class SustainabilityMetricsService
{
    private readonly ISustainabilityMetricsRepository _repository;
    private readonly OrchestratorMetrics _metrics;
    private readonly SustainabilityCalculator _calculator;
    private readonly JourneyEmitter? _journeyEmitter;
    private readonly IHomeNodeRepository? _homeNodeRepository;

    public SustainabilityMetricsService(
        ISustainabilityMetricsRepository repository,
        SustainabilityMetricsOptions options,
        OrchestratorMetrics metrics,
        SustainabilityCalculator? calculator = null,
        JourneyEmitter? journeyEmitter = null,
        IHomeNodeRepository? homeNodeRepository = null)
    {
        _repository = repository;
        _metrics = metrics;
        _calculator = calculator ?? new SustainabilityCalculator(options);
        _journeyEmitter = journeyEmitter;
        _homeNodeRepository = homeNodeRepository;
    }

    public async Task<SustainabilityRequestRecordDto?> RecordAskAsync(
        AskWorkflowContext context,
        AskResponseDto response,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var requestId = FirstNonEmpty(response.Trace.RequestId, context.RequestId, context.TraceId);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return null;
        }

        var existing = await _repository.GetByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && HasCompleteSustainabilityValues(existing))
        {
            return null;
        }

        var durationSeconds = Math.Max(0.0d, duration.TotalSeconds);
        var node = await ResolveNodeAsync(context, response, cancellationToken).ConfigureAwait(false);
        var calculation = _calculator.Calculate(durationSeconds, node.NodeWatts, node.WattsSource);
        var now = DateTimeOffset.UtcNow;
        var record = new SustainabilityRequestRecord
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            RequestId = requestId,
            TraceId = FirstNonEmpty(response.Trace.TraceId, context.TraceId, requestId),
            TimestampUtc = now,
            TenantId = Normalize(context.TenantId),
            UserIdHash = HashText(Normalize(context.UserId)),
            UserRole = "unknown",
            NodeId = node.NodeId,
            NodeName = node.NodeName,
            NodeGroup = node.NodeGroup,
            NodeRole = node.NodeRole,
            NodeWatts = calculation.NodeWatts,
            WattsSource = calculation.WattsSource,
            PrometheusInstance = node.PrometheusInstance,
            TelemetrySource = node.TelemetrySource,
            CalculationVersion = calculation.CalculationVersion,
            DurationSeconds = durationSeconds,
            EnergyWh = calculation.EnergyWh,
            EnergyValueType = calculation.EnergyValueType,
            CostUsd = calculation.CostUsd,
            CostValueType = calculation.CostValueType,
            WaterLiters = calculation.WaterLiters,
            WaterValueType = calculation.WaterValueType,
            BaselineModelVersion = calculation.BaselineModelVersion,
            CloudBaselineEnergyWh = calculation.CloudBaselineEnergyWh,
            CloudBaselineEnergyValueType = calculation.CloudBaselineEnergyValueType,
            CloudBaselineCostUsd = calculation.CloudBaselineCostUsd,
            CloudBaselineCostValueType = calculation.CloudBaselineCostValueType,
            CloudBaselineWaterLiters = calculation.CloudBaselineWaterLiters,
            CloudBaselineWaterValueType = calculation.CloudBaselineWaterValueType,
            EnergySavedWh = calculation.EnergySavedWh,
            CostSavedUsd = calculation.CostSavedUsd,
            WaterSavedLiters = calculation.WaterSavedLiters,
            SavingsValueType = calculation.SavingsValueType,
            Status = Normalize(response.ExecutionState),
            ModelOrRuntime = FirstNonEmpty(response.Trace.Model, response.ExecutionTarget, context.ModelName, "unknown"),
            Source = "orchestrator.ask",
            CreatedUtc = existing?.CreatedUtc ?? now
        };

        if (existing is null)
        {
            await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }

        _metrics.RecordSustainabilityRequest(record);
        await EmitJourneyEventAsync(record, response, cancellationToken).ConfigureAwait(false);
        return ToDto(record);
    }

    public async Task<IReadOnlyList<SustainabilityRequestRecordDto>> QueryAsync(
        string? tenantId,
        string? userIdHash,
        string? nodeId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 200);
        var records = await _repository.QueryAsync(
            NormalizeOptional(tenantId),
            NormalizeOptional(userIdHash),
            NormalizeOptional(nodeId),
            startUtc,
            endUtc,
            (normalizedPage - 1) * normalizedPageSize,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        return records.Select(ToDto).ToArray();
    }

    public static SustainabilityRequestRecordDto ToDto(SustainabilityRequestRecord record)
    {
        return new SustainabilityRequestRecordDto
        {
            RequestId = record.RequestId,
            TraceId = record.TraceId,
            TimestampUtc = record.TimestampUtc,
            TenantId = record.TenantId,
            UserIdHash = record.UserIdHash,
            UserRole = record.UserRole,
            NodeId = record.NodeId,
            NodeName = record.NodeName,
            NodeGroup = record.NodeGroup,
            NodeRole = record.NodeRole,
            NodeWatts = record.NodeWatts,
            WattsSource = record.WattsSource,
            PrometheusInstance = record.PrometheusInstance,
            TelemetrySource = record.TelemetrySource,
            CalculationVersion = record.CalculationVersion,
            DurationSeconds = record.DurationSeconds,
            EnergyWh = record.EnergyWh,
            EnergyValueType = record.EnergyValueType,
            CostUsd = record.CostUsd,
            CostValueType = record.CostValueType,
            WaterLiters = record.WaterLiters,
            WaterValueType = record.WaterValueType,
            BaselineModelVersion = record.BaselineModelVersion,
            CloudBaselineEnergyWh = record.CloudBaselineEnergyWh,
            CloudBaselineEnergyValueType = record.CloudBaselineEnergyValueType,
            CloudBaselineCostUsd = record.CloudBaselineCostUsd,
            CloudBaselineCostValueType = record.CloudBaselineCostValueType,
            CloudBaselineWaterLiters = record.CloudBaselineWaterLiters,
            CloudBaselineWaterValueType = record.CloudBaselineWaterValueType,
            EnergySavedWh = record.EnergySavedWh,
            CostSavedUsd = record.CostSavedUsd,
            WaterSavedLiters = record.WaterSavedLiters,
            SavingsValueType = record.SavingsValueType,
            Status = record.Status,
            ModelOrRuntime = record.ModelOrRuntime,
            Source = record.Source
        };
    }

    private async Task EmitJourneyEventAsync(
        SustainabilityRequestRecord record,
        AskResponseDto response,
        CancellationToken cancellationToken)
    {
        if (_journeyEmitter is null)
        {
            return;
        }

        await _journeyEmitter.EmitAsync(
            "sustainability_request",
            "completed",
            direction: "response",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-orchestrator",
            durationMs: (long)Math.Round(record.DurationSeconds * 1000.0d),
            message: "Recorded request sustainability telemetry.",
            metadata: new Dictionary<string, object?>
            {
                ["request_id"] = record.RequestId,
                ["trace_id"] = record.TraceId,
                ["energy_wh"] = record.EnergyWh,
                ["cost_usd"] = record.CostUsd,
                ["water_liters"] = record.WaterLiters,
                ["cloud_baseline_energy_wh"] = record.CloudBaselineEnergyWh,
                ["cloud_baseline_cost_usd"] = record.CloudBaselineCostUsd,
                ["cloud_baseline_water_liters"] = record.CloudBaselineWaterLiters,
                ["energy_saved_wh"] = record.EnergySavedWh,
                ["cost_saved_usd"] = record.CostSavedUsd,
                ["water_saved_liters"] = record.WaterSavedLiters,
                ["value_type"] = record.EnergyValueType,
                ["energy_value_type"] = record.EnergyValueType,
                ["cost_value_type"] = record.CostValueType,
                ["water_value_type"] = record.WaterValueType,
                ["savings_value_type"] = record.SavingsValueType,
                ["baseline_model_version"] = record.BaselineModelVersion,
                ["calculation_version"] = record.CalculationVersion,
                ["execution_target"] = FirstNonEmpty(response.Trace.ExecutionTarget, response.ExecutionTarget, record.NodeRole),
                ["node_id"] = record.NodeId,
                ["node_name"] = record.NodeName,
                ["node_group"] = record.NodeGroup,
                ["node_role"] = record.NodeRole,
                ["node_watts"] = record.NodeWatts,
                ["watts_source"] = record.WattsSource,
                ["prometheus_instance"] = record.PrometheusInstance,
                ["telemetry_source"] = record.TelemetrySource,
                ["model_or_runtime"] = record.ModelOrRuntime,
                ["status"] = record.Status
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<SustainabilityNodeIdentity> ResolveNodeAsync(
        AskWorkflowContext context,
        AskResponseDto response,
        CancellationToken cancellationToken)
    {
        var homeNodeId = FirstNonEmpty(context.HomeNodeId, response.Trace.HomeNodeId);
        if (!string.IsNullOrWhiteSpace(homeNodeId))
        {
            var homeNode = await ResolveHomeNodeAsync(context.TenantId, homeNodeId, cancellationToken).ConfigureAwait(false);
            var capabilityTelemetry = ResolveCapabilityTelemetry(homeNode?.CapabilitiesJson);
            return new SustainabilityNodeIdentity(
                homeNodeId,
                FirstNonEmpty(context.HomeNodePackName, homeNode?.DeviceClass, homeNodeId, "home_node"),
                "home_node",
                FirstNonEmpty(context.HomeNodeExecutionMode, response.Trace.HomeNodeExecutionMode, response.Trace.ExecutionTarget, response.ExecutionTarget, "home_node"),
                capabilityTelemetry.NodeWatts,
                capabilityTelemetry.WattsSource,
                NormalizeOptional(FirstNonEmpty(capabilityTelemetry.PrometheusInstance, homeNode?.TailscaleIp, context.HomeNodeTailscaleIp, response.Trace.HomeNodeTailscaleIp)),
                FirstNonEmpty(capabilityTelemetry.TelemetrySource, homeNode is null ? null : "home_node_registry", "fallback_static_estimate"));
        }

        return new SustainabilityNodeIdentity(
            "shared_compute",
            "shared_compute",
            "orchestrator",
            FirstNonEmpty(response.Trace.ExecutionTarget, response.ExecutionTarget, "shared_compute"),
            null,
            "fallback",
            null,
            "fallback_static_estimate");
    }

    private async Task<HomeNodeRecord?> ResolveHomeNodeAsync(
        string tenantId,
        string nodeId,
        CancellationToken cancellationToken)
    {
        if (_homeNodeRepository is null)
        {
            return null;
        }

        var record = await _homeNodeRepository.GetAsync(Normalize(tenantId), nodeId, cancellationToken).ConfigureAwait(false);
        return record ?? await _homeNodeRepository.FindByNodeIdAsync(nodeId, cancellationToken).ConfigureAwait(false);
    }

    private static SustainabilityCapabilityTelemetry ResolveCapabilityTelemetry(string? capabilitiesJson)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesJson))
        {
            return new SustainabilityCapabilityTelemetry(null, "fallback", null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(capabilitiesJson);
            var root = document.RootElement;
            var nodeWatts = ReadPositiveDouble(root, "node_watts")
                ?? ReadPositiveDouble(root, "estimated_node_watts")
                ?? ReadPositiveDouble(root, "sustainability.node_watts")
                ?? ReadPositiveDouble(root, "sustainability.estimated_node_watts")
                ?? ReadPositiveDouble(root, "telemetry.node_watts");
            var prometheusInstance = ReadString(root, "prometheus_instance")
                ?? ReadString(root, "telemetry.prometheus_instance")
                ?? ReadString(root, "sustainability.prometheus_instance");
            var wattsSource = nodeWatts.HasValue
                ? FirstNonEmpty(ReadString(root, "watts_source"), ReadString(root, "sustainability.watts_source"), "home_node_capabilities")
                : "fallback";
            var telemetrySource = FirstNonEmpty(
                ReadString(root, "telemetry_source"),
                ReadString(root, "sustainability.telemetry_source"),
                prometheusInstance is null ? null : "prometheus");

            return new SustainabilityCapabilityTelemetry(nodeWatts, wattsSource, prometheusInstance, telemetrySource);
        }
        catch (JsonException)
        {
            return new SustainabilityCapabilityTelemetry(null, "fallback", null, null);
        }
    }

    private static bool HasCompleteSustainabilityValues(SustainabilityRequestRecord record)
    {
        return record.EnergyWh.HasValue
            && record.CostUsd.HasValue
            && record.WaterLiters.HasValue
            && record.CloudBaselineEnergyWh.HasValue
            && record.CloudBaselineCostUsd.HasValue
            && record.CloudBaselineWaterLiters.HasValue
            && record.EnergySavedWh.HasValue
            && record.CostSavedUsd.HasValue
            && record.WaterSavedLiters.HasValue
            && record.NodeWatts.HasValue
            && !IsUnknown(record.WattsSource)
            && !IsUnknown(record.TelemetrySource)
            && !IsUnknown(record.CalculationVersion);
    }

    private static bool IsUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static double? ReadPositiveDouble(JsonElement root, string path)
    {
        if (!TryReadProperty(root, path, out var value))
        {
            return null;
        }

        double? parsed = null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var numericValue))
        {
            parsed = numericValue;
        }
        else if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var stringValue))
        {
            parsed = stringValue;
        }

        return parsed.HasValue && parsed.Value > 0.0d ? parsed : null;
    }

    private static string? ReadString(JsonElement root, string path)
    {
        if (!TryReadProperty(root, path, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static bool TryReadProperty(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var next))
            {
                return false;
            }

            value = next;
        }

        return true;
    }

    private sealed record SustainabilityNodeIdentity(
        string NodeId,
        string NodeName,
        string NodeGroup,
        string NodeRole,
        double? NodeWatts,
        string WattsSource,
        string? PrometheusInstance,
        string TelemetrySource);

    private sealed record SustainabilityCapabilityTelemetry(
        double? NodeWatts,
        string WattsSource,
        string? PrometheusInstance,
        string? TelemetrySource);
}
#nullable disable
