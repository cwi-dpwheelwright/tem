#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Recursive;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Decorators;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class RuntimeCriticExternalizationTests
{
    [Fact]
    public void BuildPayload_hashes_user_id_and_omits_raw_prompt()
    {
        var context = CreateContext();
        context.RequestId = "request-payload";
        context.TraceId = "trace-payload";
        var response = new AskResponseDto
        {
            Answer = "A response",
            Trace = new TracePayload { TraceId = "trace-payload", PolicyMode = "observe" }
        };

        var payload = ExternalRuntimeCriticClient.BuildPayload(context, response);
        var json = JsonSerializer.Serialize(payload);

        json.Should().Contain("user_id_hash");
        json.Should().NotContain("user-1");
        json.Should().NotContain("What should I do first?");
    }

    [Fact]
    public async Task External_client_posts_observation_to_configured_critic_endpoint()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "version": "runtime-critic-external-v1",
                  "mode": "observe_only",
                  "overall_score": 0.5,
                  "scores": [
                    {
                      "dimension": "synthesis_quality",
                      "score": 0.5,
                      "reason": "backend_language_in_everyday_answer"
                    }
                  ],
                  "findings": [
                    {
                      "severity": "warn",
                      "category": "synthesis",
                      "rule": "backend_language_in_everyday_answer",
                      "detail": "External Critic finding."
                    }
                  ]
                }
                """)
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://critic.local/")
        };
        var client = new ExternalRuntimeCriticClient(
            httpClient,
            new RuntimeCriticOptions
            {
                BaseUrl = "http://critic.local",
                EndpointPath = "/runtime/critic/observe"
            });
        var context = CreateContext();
        context.TraceId = "trace-http";
        context.RequestId = "request-http";
        var response = new AskResponseDto
        {
            Answer = "I do not have enough governed evidence to answer this everyday question.",
            ExecutionState = "completed",
            ExecutionTarget = "shared_compute",
            Trace = new TracePayload { TraceId = "trace-http", PolicyMode = "observe" }
        };

        var result = await client.EvaluateAsync(context, response, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/runtime/critic/observe");
        result.Should().NotBeNull();
        result!.Version.Should().Be("runtime-critic-external-v1");
        result.Findings.Should().ContainSingle(finding => finding.Rule == "backend_language_in_everyday_answer");
    }

    [Fact]
    public async Task External_client_uses_options_base_url_when_http_client_base_address_is_missing()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"version":"runtime-critic-external-v1","mode":"observe_only","overall_score":1.0}""")
        });
        var client = new ExternalRuntimeCriticClient(
            new HttpClient(handler),
            new RuntimeCriticOptions
            {
                BaseUrl = "http://critic.local",
                EndpointPath = "/runtime/critic/observe"
            });

        var result = await client.EvaluateAsync(
            CreateContext(),
            new AskResponseDto { Answer = "A response", Trace = new TracePayload() },
            CancellationToken.None);

        result.Should().NotBeNull();
        handler.Requests.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.ToString().Should().Be("http://critic.local/runtime/critic/observe");
    }

    [Fact]
    public async Task External_client_throws_clear_error_when_critic_endpoint_fails()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"error":"critic_failed"}""")
        });
        var client = new ExternalRuntimeCriticClient(
            new HttpClient(handler),
            new RuntimeCriticOptions
            {
                BaseUrl = "http://critic.local",
                EndpointPath = "/runtime/critic/observe"
            });

        var act = async () => await client.EvaluateAsync(
            CreateContext(),
            new AskResponseDto { Answer = "A response", Trace = new TracePayload() },
            CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*External runtime critic returned 500*critic_failed*");
    }

    [Fact]
    public async Task External_client_skips_http_when_critic_endpoint_is_not_configured()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var client = new ExternalRuntimeCriticClient(
            new HttpClient(handler),
            new RuntimeCriticOptions());

        var result = await client.EvaluateAsync(
            CreateContext(),
            new AskResponseDto { Answer = "A response", Trace = new TracePayload() },
            CancellationToken.None);

        result.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Decorator_records_runtime_critic_ledger_event_without_changing_answer()
    {
        var ledger = new FakeLedgerRepository();
        var decorator = new AskRuntimeCriticDecorator(
            new FakeRuntimeCriticClient(RuntimeCriticResultWithFinding("backend_language_in_everyday_answer")),
            new RuntimeCriticLedgerRecorder(
                ledger,
                new LedgerEventFactory(new NoopSignatureProvider())),
            new SelectiveEnforcementOptions(),
            NullLogger<AskRuntimeCriticDecorator>.Instance);
        var context = CreateContext();
        context.TraceId = "trace-critic";
        context.RequestId = "request-critic";
        context.FinalAnswerHash = "answer-hash";
        var response = new AskResponseDto
        {
            Answer = "I do not have enough governed evidence to answer this everyday question.",
            ExecutionState = "completed",
            ExecutionTarget = "shared_compute",
            Confidence = 0.5f,
            Trace = new TracePayload
            {
                TraceId = "trace-critic",
                Route = "model_answer",
                PolicyMode = "observe",
                Model = "model-1",
                PackHash = "pack-hash"
            }
        };

        var result = await decorator.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(response)),
            CancellationToken.None);

        result.Data!.Answer.Should().Be(response.Answer);
        ledger.Events.Should().ContainSingle(evt => evt.EventType == RuntimeCriticLedgerRecorder.EventType);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(ledger.Events.Single().PayloadJson);
        payload.Should().ContainKey("overall_score");
        payload!["event"]!.ToString().Should().Be("RUNTIME_CRITIC_OBSERVED");
    }

    [Fact]
    public async Task Decorator_records_runtime_critic_findings_as_recursive_quality_events()
    {
        var ledger = new FakeLedgerRepository();
        var recursiveRepository = new FakeRecursiveEventRepository();
        var recursiveIngest = new RecursiveEventIngestService(
            new RecursiveEventSchemaValidator(),
            recursiveRepository,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var decorator = new AskRuntimeCriticDecorator(
            new FakeRuntimeCriticClient(RuntimeCriticResultWithFinding("backend_language_in_everyday_answer")),
            new RuntimeCriticLedgerRecorder(
                ledger,
                new LedgerEventFactory(new NoopSignatureProvider()),
                recursiveIngest),
            new SelectiveEnforcementOptions(),
            NullLogger<AskRuntimeCriticDecorator>.Instance);
        var context = CreateContext();
        context.TraceId = "trace-quality";
        context.RequestId = "request-quality";
        context.FinalAnswerHash = "answer-hash";
        context.IntentType = "practical_guidance";
        context.PresentationTemplateFamily = "practical_guidance";
        var response = new AskResponseDto
        {
            Answer = "I do not have enough governed evidence to answer this everyday question.",
            ExecutionState = "completed",
            ExecutionTarget = "shared_compute",
            Confidence = 0.5f,
            Trace = new TracePayload
            {
                TraceId = "trace-quality",
                Route = "model_answer",
                PolicyMode = "observe",
                Model = "model-1",
                PackHash = "pack-hash"
            }
        };

        var result = await decorator.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(response)),
            CancellationToken.None);

        result.Data!.Answer.Should().Be(response.Answer);
        recursiveRepository.Events.Should().ContainSingle(evt =>
            evt.EventType == "quality_observation.created"
            && evt.TraceId == "trace-quality"
            && evt.SourceComponent == "runtime_critic"
            && evt.TemporalState == "current");
        ledger.Events.Should().Contain(evt => evt.EventType == RuntimeCriticLedgerRecorder.EventType);
        ledger.Events.Should().Contain(evt => evt.EventType == "recursive_event.ingested");
    }

    [Fact]
    public async Task Decorator_swallows_external_critic_failures_without_changing_answer()
    {
        var ledger = new FakeLedgerRepository();
        var decorator = new AskRuntimeCriticDecorator(
            new ThrowingRuntimeCriticClient(),
            new RuntimeCriticLedgerRecorder(
                ledger,
                new LedgerEventFactory(new NoopSignatureProvider())),
            new SelectiveEnforcementOptions(),
            NullLogger<AskRuntimeCriticDecorator>.Instance);
        var context = CreateContext();
        context.TraceId = "trace-critic-down";
        var response = new AskResponseDto
        {
            Answer = "A response",
            ExecutionState = "completed",
            ExecutionTarget = "shared_compute",
            Trace = new TracePayload { TraceId = "trace-critic-down", PolicyMode = "observe" }
        };

        var result = await decorator.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(response)),
            CancellationToken.None);

        result.Data!.Answer.Should().Be(response.Answer);
        ledger.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Decorator_skips_recording_when_external_critic_is_not_configured()
    {
        var ledger = new FakeLedgerRepository();
        var decorator = new AskRuntimeCriticDecorator(
            new FakeRuntimeCriticClient(null),
            new RuntimeCriticLedgerRecorder(
                ledger,
                new LedgerEventFactory(new NoopSignatureProvider())),
            new SelectiveEnforcementOptions(),
            NullLogger<AskRuntimeCriticDecorator>.Instance);
        var context = CreateContext();
        context.TraceId = "trace-no-critic";
        var response = new AskResponseDto
        {
            Answer = "A response",
            ExecutionState = "completed",
            ExecutionTarget = "shared_compute",
            Trace = new TracePayload { TraceId = "trace-no-critic", PolicyMode = "observe" }
        };

        var result = await decorator.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(response)),
            CancellationToken.None);

        result.Data!.Answer.Should().Be(response.Answer);
        ledger.Events.Should().BeEmpty();
    }

    private static AskWorkflowContext CreateContext()
    {
        return new AskWorkflowContext("tenant-1", "user-1", "What should I do first?", "pack", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            ModelName = "model-1",
            PackHash = "pack-hash"
        };
    }

    private static RuntimeCriticResult RuntimeCriticResultWithFinding(string rule)
    {
        return new RuntimeCriticResult(
            "runtime-critic-external-v1",
            "observe_only",
            0.5d,
            new[]
            {
                new RuntimeCriticDimensionScore("synthesis_quality", 0.5d, rule)
            },
            new[]
            {
                new RuntimeCriticFinding(
                    "warn",
                    "synthesis",
                    rule,
                    "External Critic finding.")
            });
    }

    private sealed class FakeRuntimeCriticClient : IRuntimeCriticClient
    {
        private readonly RuntimeCriticResult? _result;

        public FakeRuntimeCriticClient(RuntimeCriticResult? result)
        {
            _result = result;
        }

        public Task<RuntimeCriticResult?> EvaluateAsync(
            AskWorkflowContext context,
            AskResponseDto response,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingRuntimeCriticClient : IRuntimeCriticClient
    {
        public Task<RuntimeCriticResult?> EvaluateAsync(
            AskWorkflowContext context,
            AskResponseDto response,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("critic unavailable");
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault(evt => evt.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(
                Events.Where(evt => evt.TraceId == traceId).Skip(skip).Take(take).ToList());
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count(evt => evt.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            IEnumerable<LedgerEvent> query = Events;
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(evt => string.Equals(evt.EventType, eventType, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(query.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecursiveEventRepository : IRecursiveEventRepository
    {
        public List<RecursiveEventRecord> Events { get; } = new();
        public List<RecursiveEventQuarantineRecord> Quarantine { get; } = new();

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Any(evt => evt.EventId == eventId));
        }

        public Task AddAsync(RecursiveEventRecord record, CancellationToken cancellationToken)
        {
            Events.Add(record);
            return Task.CompletedTask;
        }

        public Task AddQuarantineAsync(RecursiveEventQuarantineRecord record, CancellationToken cancellationToken)
        {
            Quarantine.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RecursiveEventRecord>> QueryAsync(
            string? tenantId,
            string? eventType,
            string? temporalState,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<RecursiveEventRecord> query = Events;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(evt => evt.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(evt => evt.EventType == eventType);
            }

            if (!string.IsNullOrWhiteSpace(temporalState))
            {
                query = query.Where(evt => evt.TemporalState == temporalState);
            }

            return Task.FromResult<IReadOnlyList<RecursiveEventRecord>>(
                query.Skip(skip).Take(take).ToList());
        }
    }
}
#nullable disable
