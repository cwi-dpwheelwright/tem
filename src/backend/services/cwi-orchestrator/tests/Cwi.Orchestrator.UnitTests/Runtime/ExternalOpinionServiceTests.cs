#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class ExternalOpinionServiceTests
{
    [Fact]
    public async Task EvaluateAsync_does_not_call_external_provider_when_disabled_by_config()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var service = CreateService(ledger, handler, enabled: false);

        var result = await service.EvaluateAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ExternalSubmissionStatus.Should().Be("not_sent");
        result.Data.AdvisoryOnly.Should().BeTrue();
        result.Data.AppliedToAnswer.Should().BeFalse();
        result.Data.Reasons.Should().Contain("external_opinion_not_configured");
        handler.RequestBodies.Should().BeEmpty();
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "external_opinion.observed");
    }

    [Fact]
    public async Task EvaluateAsync_blocks_unapproved_policy_before_external_submission()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.OK);
        var service = CreateService(ledger, handler, enabled: true);
        var request = CreateRequest("denied");

        var result = await service.EvaluateAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ExternalSubmissionStatus.Should().Be("not_sent");
        result.Data.Reasons.Should().Contain("policy_status_not_approved");
        handler.RequestBodies.Should().BeEmpty();
        ledger.Events.Single().PayloadJson.Should().Contain("prompt_summary_hash");
        ledger.Events.Single().PayloadJson.Should().NotContain(request.PromptSummary);
    }

    [Fact]
    public async Task EvaluateAsync_sends_advisory_only_payload_and_never_applies_to_answer()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.OK, "{\"findings\":[{\"category\":\"clarity\",\"severity\":\"warning\",\"message\":\"Tighten the next action.\"}]}");
        var service = CreateService(ledger, handler, enabled: true);

        var result = await service.EvaluateAsync(CreateRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ExternalSubmissionStatus.Should().Be("submitted");
        result.Data.ExternalStatusCode.Should().Be(200);
        result.Data.AdvisoryOnly.Should().BeTrue();
        result.Data.AppliedToAnswer.Should().BeFalse();
        result.Data.Findings.Should().ContainSingle(finding =>
            finding.Category == "clarity"
            && finding.Severity == "warning");
        handler.RequestBodies.Should().ContainSingle(body =>
            body.Contains("\"advisory_only\":true", StringComparison.Ordinal)
            && body.Contains("\"prompt_summary\"", StringComparison.Ordinal));
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "external_opinion.observed");
    }

    private static ExternalOpinionRequestDto CreateRequest(string policyStatus = "approved")
    {
        return new ExternalOpinionRequestDto
        {
            TenantId = "tenant-1",
            TraceId = "trace-external-opinion",
            PolicyStatus = policyStatus,
            PromptSummary = "The user asked for a concise plan.",
            ContextSummary = "The governed response used current project context.",
            GovernedAnswerHash = "answer-hash",
            EvidenceHashes = new[] { "evidence-hash-1" }
        };
    }

    private static ExternalOpinionService CreateService(
        FakeLedgerRepository ledger,
        CaptureHandler handler,
        bool enabled)
    {
        return new ExternalOpinionService(
            new ExternalOpinionOptions
            {
                Enabled = enabled,
                BaseUrl = enabled ? "http://external-opinion" : string.Empty,
                EvaluationPath = "/evaluate"
            },
            new SelectiveEnforcementOptions(),
            new FakeHttpClientFactory(handler),
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly CaptureHandler _handler;

        public FakeHttpClientFactory(CaptureHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public CaptureHandler(HttpStatusCode statusCode, string body = "{\"findings\":[]}")
        {
            _statusCode = statusCode;
            _body = body;
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault(evt => evt.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(
            string traceId,
            int skip,
            int take,
            CancellationToken cancellationToken)
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
                query = query.Where(evt => evt.EventType == eventType);
            }

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(query.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
