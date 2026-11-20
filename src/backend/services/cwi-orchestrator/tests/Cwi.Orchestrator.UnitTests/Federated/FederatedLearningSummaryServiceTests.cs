#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Federated;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Federated;

public sealed class FederatedLearningSummaryServiceTests
{
    [Fact]
    public async Task SubmitAsync_rejects_raw_metadata_without_exporting_to_teacher()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.Accepted);
        var service = CreateService(
            ledger,
            handler,
            teacherSubmissionEnabled: true);

        var result = await service.SubmitAsync(new FederatedLearningSummaryRequestDto
        {
            TenantId = "tenant-1",
            TraceId = "trace-fed-raw",
            PolicyStatus = "approved",
            Observations = new[]
            {
                new FederatedLearningObservationDto
                {
                    Category = "routine",
                    Signal = "open_loop",
                    Metadata = new Dictionary<string, string>
                    {
                        ["raw_text"] = "private sentence that must stay local"
                    }
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.Data!.Status.Should().Be("rejected");
        result.Data.ApprovedForExport.Should().BeFalse();
        result.Data.RawContentExported.Should().BeFalse();
        handler.RequestBodies.Should().BeEmpty();
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "federated_learning.summary_rejected");
        ledger.Events.Single().PayloadJson.Should().NotContain("private sentence");
    }

    [Fact]
    public async Task SubmitAsync_observes_policy_blocked_summary_without_teacher_submission()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.Accepted);
        var service = CreateService(
            ledger,
            handler,
            teacherSubmissionEnabled: true);

        var result = await service.SubmitAsync(WithPolicy(CreateApprovedRequest(), "denied"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("observed");
        result.Data.ApprovedForExport.Should().BeFalse();
        result.Data.Reasons.Should().Contain("policy_status_not_approved");
        handler.RequestBodies.Should().BeEmpty();
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "federated_learning.summary_observed");
    }

    [Fact]
    public async Task SubmitAsync_sends_sanitized_aggregate_to_teacher_when_policy_approved()
    {
        var ledger = new FakeLedgerRepository();
        var handler = new CaptureHandler(HttpStatusCode.Accepted);
        var service = CreateService(
            ledger,
            handler,
            teacherSubmissionEnabled: true);

        var result = await service.SubmitAsync(CreateApprovedRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be("approved");
        result.Data.ApprovedForExport.Should().BeTrue();
        result.Data.TeacherSubmissionStatus.Should().Be("submitted");
        result.Data.TeacherStatusCode.Should().Be(202);
        result.Data.SummaryGroups.Should().ContainSingle(group =>
            group.Category == "routine"
            && group.Signal == "open_loop"
            && group.Count == 3);
        handler.RequestBodies.Should().ContainSingle();
        handler.RequestBodies.Single().Should().Contain("\"raw_content_exported\":false");
        handler.RequestBodies.Single().Should().NotContain("observations");
        handler.RequestBodies.Single().Should().NotContain("private");
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "federated_learning.summary_observed");
    }

    private static FederatedLearningSummaryRequestDto CreateApprovedRequest()
    {
        return new FederatedLearningSummaryRequestDto
        {
            TenantId = "tenant-1",
            UserIdHash = "user-hash",
            NodeId = "node-1",
            TraceId = "trace-fed-approved",
            PolicyStatus = "approved",
            SourceComponent = "local_process_miner",
            Observations = new[]
            {
                new FederatedLearningObservationDto
                {
                    Category = "routine",
                    Signal = "open_loop",
                    Outcome = "observed",
                    Count = 2,
                    Confidence = 0.9,
                    Tags = new[] { "morning" }
                },
                new FederatedLearningObservationDto
                {
                    Category = "routine",
                    Signal = "open_loop",
                    Outcome = "observed",
                    Count = 1,
                    Confidence = 0.6,
                    Tags = new[] { "morning", "planning" }
                }
            },
            Provenance = new FederatedLearningProvenanceDto
            {
                SourceHash = "source-hash",
                SourceId = "local-summary-1",
                GeneratedUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z")
            }
        };
    }

    private static FederatedLearningSummaryRequestDto WithPolicy(
        FederatedLearningSummaryRequestDto request,
        string policyStatus)
    {
        return new FederatedLearningSummaryRequestDto
        {
            TenantId = request.TenantId,
            UserIdHash = request.UserIdHash,
            NodeId = request.NodeId,
            TraceId = request.TraceId,
            PolicyStatus = policyStatus,
            SourceComponent = request.SourceComponent,
            Observations = request.Observations,
            Provenance = request.Provenance
        };
    }

    private static FederatedLearningSummaryService CreateService(
        FakeLedgerRepository ledger,
        CaptureHandler handler,
        bool teacherSubmissionEnabled)
    {
        return new FederatedLearningSummaryService(
            new FederatedLearningSummaryOptions
            {
                TeacherSubmissionEnabled = teacherSubmissionEnabled,
                TeacherBaseUrl = "http://teacher-node",
                TeacherSummaryPath = "/federated-learning/summaries"
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

        public CaptureHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent("{\"status\":\"accepted\"}", Encoding.UTF8, "application/json")
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
