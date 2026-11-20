#nullable enable
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Xunit;

namespace Cwi.Orchestrator.ContractTests;

public sealed class ContractSnapshotTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AskRequest_Snapshot()
    {
        AssertSnapshot<AskRequestDto>("ask-request.json");
    }

    [Fact]
    public void AskResponse_Snapshot()
    {
        AssertSnapshot<AskResponseDto>("ask-response.json");
    }

    [Fact]
    public void ActionRequest_Snapshot()
    {
        AssertSnapshot<ActionRequestDto>("action-request.json");
    }

    [Fact]
    public void ActionResponse_Snapshot()
    {
        AssertSnapshot<ActionResponseDto>("action-response.json");
    }

    [Fact]
    public void TelemetryIngestRequest_Snapshot()
    {
        AssertSnapshot<TelemetryIngestRequestDto>("telemetry-ingest-request.json");
    }

    [Fact]
    public void TelemetryIngestResponse_Snapshot()
    {
        AssertSnapshot<TelemetryIngestResponseDto>("telemetry-ingest-response.json");
    }

    [Fact]
    public void ApprovalCompleteRequest_Snapshot()
    {
        AssertSnapshot<ApprovalCompleteRequestDto>("approval-complete-request.json");
    }

    [Fact]
    public void ApprovalCompleteResponse_Snapshot()
    {
        AssertSnapshot<ApprovalCompleteResponseDto>("approval-complete-response.json");
    }

    [Fact]
    public void ApprovalTicketStatus_Snapshot()
    {
        AssertSnapshot<ApprovalTicketStatusDto>("approval-ticket-status.json");
    }

    [Fact]
    public void TracePayload_Governance_Snapshot()
    {
        AssertSnapshot<TracePayload>("trace-governance.json");
    }

    [Fact]
    public void OutcomeFeedbackRequest_Snapshot()
    {
        AssertSnapshot<OutcomeFeedbackRequestDto>("outcome-feedback-request.json");
    }

    [Fact]
    public void OutcomeFeedbackSummary_Snapshot()
    {
        AssertSnapshot<OutcomeFeedbackSummaryDto>("outcome-feedback-summary.json");
    }

    [Fact]
    public void OutcomeFeedbackResponse_Snapshot()
    {
        AssertSnapshot<OutcomeFeedbackResponseDto>("outcome-feedback-response.json");
    }

    [Fact]
    public void RecursiveEventIngestResponse_Snapshot()
    {
        AssertWebSnapshot<RecursiveEventIngestResponseDto>("recursive-event-ingest-response.json");
    }

    [Fact]
    public void RecursiveEventRecord_Snapshot()
    {
        AssertWebSnapshot<RecursiveEventRecordDto>("recursive-event-record-response.json");
    }

    [Fact]
    public void GraphUpdateCandidate_Snapshot()
    {
        AssertWebSnapshot<GraphUpdateCandidateDto>("graph-update-candidate-response.json");
    }

    [Fact]
    public void GraphVectorRefreshJob_Snapshot()
    {
        AssertWebSnapshot<GraphVectorRefreshJobDto>("graph-vector-refresh-job-response.json");
    }

    [Fact]
    public void ResponseQualityGraph_Snapshot()
    {
        AssertWebSnapshot<ResponseQualityGraphDto>("response-quality-graph-response.json");
    }

    private static void AssertSnapshot<T>(string filename)
    {
        AssertSnapshot<T>(filename, options: null);
    }

    private static void AssertWebSnapshot<T>(string filename)
    {
        AssertSnapshot<T>(filename, WebJsonOptions);
    }

    private static void AssertSnapshot<T>(string filename, JsonSerializerOptions? options)
    {
        var path = Path.Combine(ResolveFixtureRoot(), filename);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        var value = JsonSerializer.Deserialize<T>(json, options);

        Assert.NotEqual(default, value);

        var serialized = JsonSerializer.Serialize(value, options);
        var canonicalExpected = Canonicalize(element);
        var canonicalActual = Canonicalize(JsonSerializer.Deserialize<JsonElement>(serialized));

        Assert.Equal(canonicalExpected, canonicalActual);
    }

    private static string ResolveFixtureRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "contracts", "fixtures", "orchestrator");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate contracts/fixtures/orchestrator.");
    }

    private static string Canonicalize(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element),
            JsonValueKind.Array => CanonicalizeArray(element),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string CanonicalizeObject(JsonElement element)
    {
        var props = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            builder.Append(JsonSerializer.Serialize(prop.Name));
            builder.Append(':');
            builder.Append(Canonicalize(prop.Value));
            if (i < props.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static string CanonicalizeArray(JsonElement element)
    {
        var items = element.EnumerateArray().ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(Canonicalize(items[i]));
            if (i < items.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append(']');
        return builder.ToString();
    }
}
#nullable disable
