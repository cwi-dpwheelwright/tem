#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Decorators;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Telemetry;

public sealed class SustainabilityMetricsServiceTests
{
    [Fact]
    public async Task RecordAskAsync_stores_seeded_estimates_and_user_hash_without_user_prometheus_label()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var metrics = new OrchestratorMetrics();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions(),
            metrics);
        var context = CreateContext();
        var response = CreateResponse();

        var record = await service.RecordAskAsync(
            context,
            response,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        record.Should().NotBeNull();
        record!.UserIdHash.Should().NotBe("user-1");
        record.EnergyWh.Should().BeApproximately(0.033333333d, 0.000001d);
        record.EnergyValueType.Should().Be("estimated");
        record.NodeWatts.Should().Be(SustainabilityMetricsOptions.DefaultEstimatedNodeWatts);
        record.WattsSource.Should().Be("fallback");
        record.TelemetrySource.Should().Be("fallback_static_estimate");
        record.BaselineModelVersion.Should().Be(SustainabilityMetricsOptions.DefaultCloudBaselineModelVersion);
        record.CloudBaselineEnergyWh.Should().Be(SustainabilityMetricsOptions.DefaultCloudBaselineEnergyWhPerRequest);
        record.EnergySavedWh.Should().NotBeNull();
        repository.Records.Should().ContainSingle(item => item.RequestId == "request-1");
        var aggregate = metrics.Snapshot().Sustainability.Should().ContainSingle().Subject;
        aggregate.TenantId.Should().Be("tenant-1");
        aggregate.NodeId.Should().Be("shared_compute");
        aggregate.RequestsTotal.Should().Be(1);
    }

    [Fact]
    public async Task RecordAskAsync_marks_energy_and_cost_as_estimated_when_estimate_options_are_configured()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions
            {
                EstimatedNodeWatts = 180,
                CostUsdPerKwh = 0.12
            },
            new OrchestratorMetrics());

        var record = await service.RecordAskAsync(
            CreateContext(),
            CreateResponse(),
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        record!.EnergyWh.Should().BeApproximately(1.0d, 0.000001d);
        record.EnergyValueType.Should().Be("estimated");
        record.CostUsd.Should().BeApproximately(0.00012d, 0.000001d);
        record.CostValueType.Should().Be("estimated");
    }

    [Fact]
    public async Task RecordAskAsync_records_versioned_cloud_baseline_and_savings_when_configured()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var metrics = new OrchestratorMetrics();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions
            {
                EstimatedNodeWatts = 180,
                CostUsdPerKwh = 0.12,
                EstimatedWaterLitersPerRequest = 0.005,
                CloudBaselineModelVersion = "cloud-baseline-test.v1",
                CloudBaselineEnergyWhPerRequest = 3,
                CloudBaselineCostUsdPerRequest = 0.001,
                CloudBaselineWaterLitersPerRequest = 0.02
            },
            metrics);

        var record = await service.RecordAskAsync(
            CreateContext(),
            CreateResponse(),
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        record!.BaselineModelVersion.Should().Be("cloud-baseline-test.v1");
        record.CloudBaselineEnergyWh.Should().Be(3);
        record.CloudBaselineEnergyValueType.Should().Be("estimated");
        record.CloudBaselineCostUsd.Should().Be(0.001);
        record.CloudBaselineCostValueType.Should().Be("estimated");
        record.CloudBaselineWaterLiters.Should().Be(0.02);
        record.CloudBaselineWaterValueType.Should().Be("estimated");
        record.WaterLiters.Should().Be(0.005);
        record.WaterValueType.Should().Be("estimated");
        record.EnergySavedWh.Should().BeApproximately(2.0d, 0.000001d);
        record.CostSavedUsd.Should().BeApproximately(0.00088d, 0.000001d);
        record.WaterSavedLiters.Should().BeApproximately(0.015d, 0.000001d);
        record.SavingsValueType.Should().Be("estimated");

        var aggregate = metrics.Snapshot().Sustainability.Should().ContainSingle().Subject;
        aggregate.CloudBaselineEnergyWhTotal.Should().Be(3);
        aggregate.CloudBaselineCostUsdTotal.Should().Be(0.001);
        aggregate.CloudBaselineWaterLitersTotal.Should().Be(0.02);
        aggregate.EnergySavedWhTotal.Should().BeApproximately(2.0d, 0.000001d);
        aggregate.CostSavedUsdTotal.Should().BeApproximately(0.00088d, 0.000001d);
        aggregate.WaterSavedLitersTotal.Should().BeApproximately(0.015d, 0.000001d);
    }

    [Fact]
    public async Task AskMetricsDecorator_records_populated_sustainability_metrics_after_successful_response()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions
            {
                EstimatedNodeWatts = 180,
                CostUsdPerKwh = 0.12,
                EstimatedWaterLitersPerRequest = 0.005,
                CloudBaselineModelVersion = "cloud-baseline-test.v1",
                CloudBaselineEnergyWhPerRequest = 3,
                CloudBaselineCostUsdPerRequest = 0.001,
                CloudBaselineWaterLitersPerRequest = 0.02
            },
            new OrchestratorMetrics());
        var decorator = new AskMetricsDecorator(
            service,
            NullLogger<AskMetricsDecorator>.Instance);

        var result = await decorator.ExecuteAsync(
            CreateContext(),
            async () =>
            {
                await Task.Delay(1);
                return ServiceResponse<AskResponseDto>.Success(CreateResponse());
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Records.Should().ContainSingle();
        var persisted = repository.Records.Single();
        persisted.EnergyWh.Should().NotBeNull();
        persisted.CostUsd.Should().NotBeNull();
        persisted.WaterLiters.Should().Be(0.005);
        persisted.CloudBaselineEnergyWh.Should().Be(3);
        persisted.CloudBaselineCostUsd.Should().Be(0.001);
        persisted.CloudBaselineWaterLiters.Should().Be(0.02);
        persisted.EnergySavedWh.Should().NotBeNull();
        persisted.CostSavedUsd.Should().NotBeNull();
        persisted.WaterSavedLiters.Should().BeApproximately(0.015d, 0.000001d);
    }

    [Fact]
    public async Task RecordAskAsync_is_idempotent_per_request_id()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions(),
            new OrchestratorMetrics());

        var first = await service.RecordAskAsync(CreateContext(), CreateResponse(), TimeSpan.FromSeconds(1), CancellationToken.None);
        var second = await service.RecordAskAsync(CreateContext(), CreateResponse(), TimeSpan.FromSeconds(1), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull();
        repository.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordAskAsync_enriches_existing_sparse_request_record()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        repository.Records.Add(new SustainabilityRequestRecord
        {
            Id = Guid.NewGuid(),
            RequestId = "request-1",
            TraceId = "trace-1",
            TimestampUtc = DateTimeOffset.UtcNow,
            TenantId = "tenant-1",
            UserIdHash = "old",
            UserRole = "unknown",
            NodeId = "shared_compute",
            NodeName = "shared_compute",
            NodeGroup = "orchestrator",
            NodeRole = "shared_compute",
            DurationSeconds = 0,
            EnergyValueType = "unknown",
            CostValueType = "unknown",
            WaterValueType = "unknown",
            BaselineModelVersion = "none",
            CloudBaselineEnergyValueType = "unknown",
            CloudBaselineCostValueType = "unknown",
            CloudBaselineWaterValueType = "unknown",
            SavingsValueType = "unknown",
            Status = "completed",
            ModelOrRuntime = "model-1",
            Source = "orchestrator.ask",
            CreatedUtc = DateTimeOffset.UtcNow
        });
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions(),
            new OrchestratorMetrics());

        var record = await service.RecordAskAsync(
            CreateContext(),
            CreateResponse(),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        record.Should().NotBeNull();
        repository.Records.Should().ContainSingle();
        var persisted = repository.Records.Single();
        persisted.EnergyWh.Should().NotBeNull();
        persisted.CostUsd.Should().NotBeNull();
        persisted.WaterSavedLiters.Should().NotBeNull();
        persisted.UserIdHash.Should().NotBe("old");
    }

    [Fact]
    public async Task RecordAskAsync_uses_trace_home_node_when_context_home_node_is_missing()
    {
        var repository = new FakeSustainabilityMetricsRepository();
        var service = new SustainabilityMetricsService(
            repository,
            new SustainabilityMetricsOptions(),
            new OrchestratorMetrics());

        var record = await service.RecordAskAsync(
            CreateContext(),
            CreateResponse(homeNodeId: "home-node-01", executionTarget: "home_node"),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        record!.NodeId.Should().Be("home-node-01");
        record.NodeGroup.Should().Be("home_node");
        record.NodeRole.Should().Be("home_node");
    }

    private static AskWorkflowContext CreateContext()
    {
        return new AskWorkflowContext("tenant-1", "user-1", "question", "pack", null, "observe", 3)
        {
            RequestId = "request-1",
            TraceId = "trace-1",
            ModelName = "model-1"
        };
    }

    private static AskResponseDto CreateResponse(
        string? homeNodeId = null,
        string executionTarget = "shared_compute")
    {
        return new AskResponseDto
        {
            Answer = "answer",
            ExecutionState = "completed",
            ExecutionTarget = executionTarget,
            Trace = new TracePayload
            {
                RequestId = "request-1",
                TraceId = "trace-1",
                Model = "model-1",
                PolicyMode = "observe",
                ExecutionTarget = executionTarget,
                HomeNodeId = homeNodeId
            }
        };
    }

    private sealed class FakeSustainabilityMetricsRepository : ISustainabilityMetricsRepository
    {
        public List<SustainabilityRequestRecord> Records { get; } = new();

        public Task<SustainabilityRequestRecord?> GetByRequestIdAsync(
            string requestId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Records.FirstOrDefault(record => record.RequestId == requestId));
        }

        public Task AddAsync(
            SustainabilityRequestRecord record,
            CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task UpsertAsync(
            SustainabilityRequestRecord record,
            CancellationToken cancellationToken)
        {
            var index = Records.FindIndex(item => item.RequestId == record.RequestId);
            if (index >= 0)
            {
                Records[index] = record;
            }
            else
            {
                Records.Add(record);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SustainabilityRequestRecord>> QueryAsync(
            string? tenantId,
            string? userIdHash,
            string? nodeId,
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<SustainabilityRequestRecord> query = Records;
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

            return Task.FromResult<IReadOnlyList<SustainabilityRequestRecord>>(
                query.Skip(skip).Take(take).ToList());
        }
    }
}
#nullable disable
