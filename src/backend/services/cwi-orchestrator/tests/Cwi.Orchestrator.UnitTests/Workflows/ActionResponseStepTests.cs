using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class ActionResponseStepTests
{
    [Fact]
    public async Task ExecuteAsync_populates_trace_model_and_pack_hash()
    {
        var traceStore = new TraceLookupStore();
        var step = new ActionResponseStep(traceStore);

        var context = new ActionWorkflowContext(
            "tenant",
            "user",
            "approve",
            "risk_demo",
            new Dictionary<string, object?>())
        {
            ModelName = "model-1",
            PackHash = "pack-hash-1",
            PackVersion = "1.0.0"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Trace.Model.Should().Be("model-1");
        response.Data.Trace.PackHash.Should().Be("pack-hash-1");
        response.Data.Trace.PackVersion.Should().Be("1.0.0");
    }
}
