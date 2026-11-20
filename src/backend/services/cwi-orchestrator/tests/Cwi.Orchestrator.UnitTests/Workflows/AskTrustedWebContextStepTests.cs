using System;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskTrustedWebContextStepTests
{
    [Fact]
    public async Task ExecuteAsync_includes_trusted_web_without_changing_route_or_response_mode()
    {
        var provider = new FakeTrustedWebProvider
        {
            Result = new TrustedWebContextResult(
                new[]
                {
                    new TrustedWebResult
                    {
                        SourceId = "trusted-docs",
                        SourceName = "Trusted Docs",
                        Url = "https://example.com/docs",
                        Title = "Trusted docs",
                        Snippet = "Trusted public context.",
                        ChunkId = "docs",
                        Score = 0.9f
                    }
                },
                "general")
        };
        var step = new AskTrustedWebContextStep(
            new TrustedWebContextOptions { Enabled = true },
            provider,
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "what is oceaneering", "pack", null, "observe", 3)
        {
            TrustedWebContextRequested = true,
            TrustedWebContextMode = "include",
            QueryClassification = "general_model_knowledge",
            IntentType = "direct_answer",
            Route = "general_model_knowledge",
            TemplateUsed = "direct_answer"
        };

        var nextCalled = false;
        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        provider.Calls.Should().Be(1);
        context.TrustedWebContextEnabled.Should().BeTrue();
        context.TrustedWebResults.Should().ContainSingle(result => result.SourceId == "trusted-docs");
        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("direct_answer");
        context.Route.Should().Be("general_model_knowledge");
        context.TemplateUsed.Should().Be("direct_answer");
        context.SkipModelInference.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_skips_provider_when_globally_disabled()
    {
        var provider = new FakeTrustedWebProvider();
        var step = new AskTrustedWebContextStep(
            new TrustedWebContextOptions { Enabled = false },
            provider,
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            TrustedWebContextRequested = true,
            TrustedWebContextMode = "include"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        provider.Calls.Should().Be(0);
        context.TrustedWebContextEnabled.Should().BeFalse();
        context.TrustedWebContextUnavailable.Should().BeTrue();
        context.TrustedWebContextSkippedReason.Should().Be("trusted_web_context_disabled");
    }

    [Fact]
    public async Task ExecuteAsync_skips_provider_when_request_toggle_is_off()
    {
        var provider = new FakeTrustedWebProvider
        {
            Result = new TrustedWebContextResult(
                new[]
                {
                    new TrustedWebResult
                    {
                        SourceId = "trusted-docs",
                        Url = "https://example.com/docs",
                        Title = "Trusted docs",
                        Snippet = "This should not be fetched when the request toggle is off.",
                        ChunkId = "docs",
                        Score = 0.9f
                    }
                },
                "general")
        };
        var step = new AskTrustedWebContextStep(
            new TrustedWebContextOptions { Enabled = true },
            provider,
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            TrustedWebContextRequested = false,
            TrustedWebContextMode = "include"
        };

        var nextCalled = false;
        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        provider.Calls.Should().Be(0);
        context.TrustedWebContextEnabled.Should().BeFalse();
        context.TrustedWebResults.Should().BeEmpty();
        context.TrustedWebContextUnavailable.Should().BeFalse();
        context.SkipModelInference.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_soft_fails_provider_errors_and_continues_native_path()
    {
        var provider = new FakeTrustedWebProvider { Throw = true };
        var step = new AskTrustedWebContextStep(
            new TrustedWebContextOptions { Enabled = true },
            provider,
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            TrustedWebContextRequested = true,
            TrustedWebContextMode = "include"
        };

        var nextCalled = false;
        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.TrustedWebContextUnavailable.Should().BeTrue();
        context.TrustedWebContextSkippedReason.Should().Be("trusted_web_context_unavailable");
        context.ResponseWarnings.Should().Contain("trusted_web_context_unavailable");
    }

    private sealed class FakeTrustedWebProvider : ITrustedWebContextProvider
    {
        public int Calls { get; private set; }
        public bool Throw { get; init; }
        public TrustedWebContextResult Result { get; init; } = new(Array.Empty<TrustedWebResult>(), "general");

        public Task<TrustedWebContextResult> RetrieveAsync(
            TrustedWebContextRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw)
            {
                throw new InvalidOperationException("provider failed");
            }

            return Task.FromResult(Result);
        }
    }
}
