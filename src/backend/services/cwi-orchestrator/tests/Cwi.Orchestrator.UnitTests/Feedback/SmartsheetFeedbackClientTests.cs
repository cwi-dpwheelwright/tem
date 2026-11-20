using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application.Feedback;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Feedback;

public sealed class SmartsheetFeedbackClientTests
{
    private static readonly (long Id, string Title)[] CriticalColumns =
    [
        (101, "Submitted At"),
        (102, "Request ID"),
        (103, "Trace ID"),
        (104, "Question"),
        (105, "Answer"),
        (106, "Feedback Type"),
        (107, "Severity"),
        (108, "Status"),
        (109, "Screenshot Link")
    ];

    [Fact]
    public async Task CreateRowAsync_fetches_sheet_metadata_and_posts_row_by_column_title()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse(BuildMetadata(CriticalColumns.Concat([(110, "Tenant ID"), (111, "Priority")])));
            }

            return JsonResponse("""{"result":[{"id":"row-123"}]}""");
        });
        var client = CreateClient(handler);

        var result = await client.CreateRowAsync(new Dictionary<string, object?>
        {
            ["Submitted At"] = "2026-05-01T02:30:00Z",
            ["Tenant ID"] = "tenant-1",
            ["Request ID"] = "request-1",
            ["Trace ID"] = "trace-1",
            ["Question"] = "what is todays date",
            ["Answer"] = "Today is Friday, May 1, 2026.",
            ["Feedback Type"] = "Wrong answer",
            ["Severity"] = "Medium",
            ["Status"] = "New",
            ["Priority"] = "Medium",
            ["Screenshot Link"] = "cwi://feedback-screenshots/tenant/screenshot.png",
            ["Column Not In Sheet"] = "ignored"
        }, CancellationToken.None);

        result.RowId.Should().Be("row-123");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].Uri.Should().EndWith("/sheets/805047096004484");
        handler.Requests[1].Method.Should().Be(HttpMethod.Post);
        handler.Requests[1].Uri.Should().EndWith("/sheets/805047096004484/rows");
        handler.Requests
            .All(request =>
                request.Authorization != null
                && request.Authorization.Scheme == "Bearer"
                && request.Authorization.Parameter == "secret-token")
            .Should()
            .BeTrue();

        using var document = JsonDocument.Parse(handler.Requests[1].Body!);
        var cells = document.RootElement[0].GetProperty("cells").EnumerateArray().ToArray();
        cells.Should().Contain(cell =>
            cell.GetProperty("columnId").GetInt64() == 106
            && cell.GetProperty("value").GetString() == "Wrong answer");
        cells.Should().Contain(cell =>
            cell.GetProperty("columnId").GetInt64() == 111
            && cell.GetProperty("value").GetString() == "Medium");
        cells.Should().NotContain(cell => cell.GetProperty("value").GetString() == "ignored");
    }

    [Fact]
    public async Task CreateRowAsync_skips_when_sheet_is_missing_critical_columns()
    {
        var handler = new RecordingHandler(request =>
            JsonResponse(BuildMetadata(CriticalColumns.Where(column => column.Title != "Trace ID"))));
        var client = CreateClient(handler);

        var result = await client.CreateRowAsync(new Dictionary<string, object?>
        {
            ["Submitted At"] = "2026-05-01T02:30:00Z",
            ["Request ID"] = "request-1",
            ["Trace ID"] = "trace-1",
            ["Question"] = "question",
            ["Answer"] = "answer",
            ["Feedback Type"] = "Wrong answer",
            ["Severity"] = "Medium",
            ["Status"] = "New",
            ["Screenshot Link"] = ""
        }, CancellationToken.None);

        result.RowId.Should().BeNull();
        result.SkippedReason.Should().Be("smartsheet_missing_critical_columns:Trace ID");
        handler.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CreateRowAsync_can_use_preconfigured_column_ids_without_metadata_lookup()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"result":[{"id":987654321}]}"""));
        var options = new FeedbackReportOptions
        {
            SmartsheetEnabled = true,
            SmartsheetBaseUrl = "https://api.smartsheet.com/2.0",
            SmartsheetSheetId = "805047096004484",
            SmartsheetApiToken = "secret-token",
            SmartsheetColumnIds = CriticalColumns.ToDictionary(
                column => column.Title,
                column => column.Id,
                StringComparer.OrdinalIgnoreCase)
        };
        var client = new SmartsheetFeedbackClient(new SingleClientFactory(handler), options);

        var result = await client.CreateRowAsync(new Dictionary<string, object?>
        {
            ["Submitted At"] = "2026-05-01T02:30:00Z",
            ["Request ID"] = "request-1",
            ["Trace ID"] = "trace-1",
            ["Question"] = "question",
            ["Answer"] = "answer",
            ["Feedback Type"] = "Wrong answer",
            ["Severity"] = "Medium",
            ["Status"] = "New",
            ["Screenshot Link"] = ""
        }, CancellationToken.None);

        result.RowId.Should().Be("987654321");
        handler.Requests.Should().ContainSingle(request => request.Method == HttpMethod.Post);
    }

    private static SmartsheetFeedbackClient CreateClient(RecordingHandler handler)
    {
        return new SmartsheetFeedbackClient(new SingleClientFactory(handler), BuildOptions());
    }

    private static FeedbackReportOptions BuildOptions()
    {
        return new FeedbackReportOptions
        {
            SmartsheetEnabled = true,
            SmartsheetBaseUrl = "https://api.smartsheet.com/2.0",
            SmartsheetSheetId = "805047096004484",
            SmartsheetApiToken = "secret-token"
        };
    }

    private static string BuildMetadata(IEnumerable<(long Id, string Title)> columns)
    {
        return JsonSerializer.Serialize(new
        {
            columns = columns.Select(column => new
            {
                id = column.Id,
                title = column.Title
            })
        });
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public SingleClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.smartsheet.com/2.0/")
            };
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization,
                body));
            return _respond(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        AuthenticationHeaderValue? Authorization,
        string? Body);
}
