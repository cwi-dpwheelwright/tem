using System.Text.Json;
using Cwi.Orchestrator.Application.Feedback;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Feedback;

public sealed class FeedbackReportServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"cwi-feedback-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SubmitAsync_records_local_receipt_without_screenshot_when_smartsheet_is_disabled()
    {
        var service = CreateService(new FakeSmartsheetClient(new SmartsheetFeedbackRowResult(null, "smartsheet_disabled")));
        var request = BuildRequest(screenshot: null);

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Ok.Should().BeTrue();
        result.Data.ScreenshotStored.Should().BeFalse();
        result.Data.Warnings.Should().Contain("smartsheet_disabled");
        var audit = ReadAuditLines().Should().ContainSingle().Subject;
        audit.GetProperty("event_type").GetString().Should().Be("FEEDBACK_SUBMITTED");
        audit.GetProperty("feedback_id").GetString().Should().Be(result.Data.FeedbackId);
    }

    [Fact]
    public async Task SubmitAsync_stores_png_screenshot_and_hashes_it()
    {
        var service = CreateService(new FakeSmartsheetClient(new SmartsheetFeedbackRowResult("row-123")));
        var png = Convert.ToBase64String(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 });
        var request = BuildRequest(new FeedbackScreenshotDto
        {
            Included = true,
            DataUrl = $"data:image/png;base64,{png}"
        });

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ScreenshotStored.Should().BeTrue();
        result.Data.SmartsheetRowId.Should().Be("row-123");
        Directory.GetFiles(Path.Combine(_tempRoot, "screenshots"), "*", SearchOption.AllDirectories).Should().ContainSingle();
        var audit = ReadAuditLines().Should().ContainSingle().Subject;
        audit.GetProperty("screenshot_stored").GetBoolean().Should().BeTrue();
        audit.GetProperty("screenshot_sha256").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SubmitAsync_continues_when_screenshot_is_invalid()
    {
        var service = CreateService(new FakeSmartsheetClient(new SmartsheetFeedbackRowResult("row-124")));
        var request = BuildRequest(new FeedbackScreenshotDto
        {
            Included = true,
            DataUrl = "data:text/plain;base64,abc123"
        });

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ScreenshotStored.Should().BeFalse();
        result.Data.Message.Should().Be("Feedback submitted without screenshot.");
        result.Data.Warnings.Should().Contain("screenshot_invalid_or_too_large");
    }

    [Fact]
    public async Task SubmitAsync_records_local_receipt_when_smartsheet_submit_fails()
    {
        var service = CreateService(new FakeSmartsheetClient(new SmartsheetFeedbackRowResult(null)) { Throw = true });
        var request = BuildRequest(screenshot: null);

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Message.Should().Be("Feedback captured locally.");
        result.Data.Warnings.Should().Contain("smartsheet_submit_failed");
        var audit = ReadAuditLines().Should().ContainSingle().Subject;
        audit.GetProperty("event_type").GetString().Should().Be("FEEDBACK_SUBMITTED");
        audit.GetProperty("warnings").EnumerateArray().Select(value => value.GetString()).Should().Contain("smartsheet_submit_failed");
    }

    [Fact]
    public async Task SubmitAsync_maps_payload_to_smartsheet_intake_columns()
    {
        var fake = new FakeSmartsheetClient(new SmartsheetFeedbackRowResult("row-125"));
        var service = CreateService(fake);
        var request = BuildRequest(screenshot: null, type: "Wrong memory used", severity: "High");

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fake.LastValues.Should().NotBeNull();
        fake.LastValues!["Submitted At"].Should().NotBeNull();
        fake.LastValues["Tenant ID"].Should().Be("tenant-1");
        fake.LastValues["User ID"].Should().Be("user-1");
        fake.LastValues["Request ID"].Should().Be("request-1");
        fake.LastValues["Trace ID"].Should().Be("trace-1");
        fake.LastValues["Question"].Should().Be("what is 2 plus 2");
        fake.LastValues["Answer"].Should().Be("2 + 2 = 4.");
        fake.LastValues["Feedback Type"].Should().Be("Memory issue");
        fake.LastValues["Severity"].Should().Be("High");
        fake.LastValues["Status"].Should().Be("New");
        fake.LastValues["Priority"].Should().Be("High");
        fake.LastValues["Route"].Should().Be("deterministic_direct_answer");
        fake.LastValues["Intent"].Should().Be("simple_arithmetic_intent");
        fake.LastValues["Screenshot Link"].Should().BeNull();
        fake.LastValues["Screenshot SHA256"].Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_keeps_helpful_feedback_local_only_until_smartsheet_dropdown_supports_it()
    {
        var fake = new FakeSmartsheetClient(new SmartsheetFeedbackRowResult("row-126"));
        var service = CreateService(fake);
        var request = BuildRequest(screenshot: null, type: "Helpful", severity: "Low");

        var result = await service.SubmitAsync(request, "tenant-1", "user-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.SmartsheetRowId.Should().BeNull();
        result.Data.Warnings.Should().Contain("smartsheet_helpful_feedback_local_only");
        fake.CallCount.Should().Be(0);
        var audit = ReadAuditLines().Should().ContainSingle().Subject;
        audit.GetProperty("feedback_type").GetString().Should().Be("Helpful");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private FeedbackReportService CreateService(FakeSmartsheetClient smartsheetClient)
    {
        return new FeedbackReportService(
            new FeedbackReportOptions
            {
                ScreenshotStorageDirectory = Path.Combine(_tempRoot, "screenshots"),
                ScreenshotReferenceBaseUri = "cwi://feedback-screenshots/",
                AuditLedgerPath = Path.Combine(_tempRoot, "ledger", "feedback_events.jsonl"),
                SmartsheetEnabled = true
            },
            smartsheetClient);
    }

    private IReadOnlyList<JsonElement> ReadAuditLines()
    {
        var path = Path.Combine(_tempRoot, "ledger", "feedback_events.jsonl");
        return File.ReadAllLines(path)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
    }

    private static FeedbackReportRequestDto BuildRequest(
        FeedbackScreenshotDto? screenshot,
        string type = "Wrong answer",
        string severity = "Medium")
    {
        return new FeedbackReportRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            RequestId = "request-1",
            TraceId = "trace-1",
            Question = "what is 2 plus 2",
            Answer = "2 + 2 = 4.",
            Confidence = 0.95,
            Feedback = new FeedbackUserInputDto
            {
                Type = type,
                Severity = severity,
                Comment = "Expected a shorter answer."
            },
            RuntimeTrace = new FeedbackRuntimeTraceDto
            {
                Route = "deterministic_direct_answer",
                Intent = "simple_arithmetic_intent"
            },
            Screenshot = screenshot
        };
    }

    private sealed class FakeSmartsheetClient : ISmartsheetFeedbackClient
    {
        private readonly SmartsheetFeedbackRowResult _result;

        public FakeSmartsheetClient(SmartsheetFeedbackRowResult result)
        {
            _result = result;
        }

        public bool Throw { get; init; }
        public int CallCount { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastValues { get; private set; }

        public Task<SmartsheetFeedbackRowResult> CreateRowAsync(
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastValues = values;
            if (Throw)
            {
                throw new InvalidOperationException("smartsheet failed");
            }

            values.Should().ContainKey("Feedback Type");
            return Task.FromResult(_result);
        }
    }
}
