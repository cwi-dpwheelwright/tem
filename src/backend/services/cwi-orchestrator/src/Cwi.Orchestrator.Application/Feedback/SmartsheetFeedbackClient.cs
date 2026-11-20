using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Feedback;

public sealed record SmartsheetFeedbackRowResult(string? RowId, string? SkippedReason = null);

public interface ISmartsheetFeedbackClient
{
    Task<SmartsheetFeedbackRowResult> CreateRowAsync(
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken);
}

public sealed class SmartsheetFeedbackClient : ISmartsheetFeedbackClient
{
    private static readonly string[] CriticalColumns =
    [
        "Submitted At",
        "Request ID",
        "Trace ID",
        "Question",
        "Answer",
        "Feedback Type",
        "Severity",
        "Status",
        "Screenshot Link"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FeedbackReportOptions _options;

    public SmartsheetFeedbackClient(IHttpClientFactory httpClientFactory, FeedbackReportOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<SmartsheetFeedbackRowResult> CreateRowAsync(
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (!_options.SmartsheetEnabled)
        {
            return new SmartsheetFeedbackRowResult(null, "smartsheet_disabled");
        }

        if (string.IsNullOrWhiteSpace(_options.SmartsheetSheetId)
            || string.IsNullOrWhiteSpace(_options.SmartsheetApiToken))
        {
            return new SmartsheetFeedbackRowResult(null, "smartsheet_not_configured");
        }

        var columnIds = await ResolveColumnIdsAsync(cancellationToken).ConfigureAwait(false);
        var missingCriticalColumns = CriticalColumns
            .Where(column => !columnIds.ContainsKey(column))
            .ToArray();
        if (missingCriticalColumns.Length > 0)
        {
            return new SmartsheetFeedbackRowResult(
                null,
                $"smartsheet_missing_critical_columns:{string.Join(",", missingCriticalColumns)}");
        }

        var cells = new List<object>();
        foreach (var (columnName, value) in values)
        {
            if (value is null || !columnIds.TryGetValue(columnName, out var columnId))
            {
                continue;
            }

            cells.Add(new
            {
                columnId,
                value
            });
        }

        if (cells.Count == 0)
        {
            return new SmartsheetFeedbackRowResult(null, "smartsheet_columns_not_mapped");
        }

        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                toBottom = true,
                cells
            }
        });

        var client = _httpClientFactory.CreateClient("smartsheet-feedback");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"sheets/{Uri.EscapeDataString(_options.SmartsheetSheetId)}/rows");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SmartsheetApiToken);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Smartsheet row creation failed with status {(int)response.StatusCode}.");
        }

        return new SmartsheetFeedbackRowResult(TryReadRowId(body));
    }

    private async Task<IReadOnlyDictionary<string, long>> ResolveColumnIdsAsync(CancellationToken cancellationToken)
    {
        var columnIds = new Dictionary<string, long>(_options.SmartsheetColumnIds, StringComparer.OrdinalIgnoreCase);
        if (CriticalColumns.All(columnIds.ContainsKey))
        {
            return columnIds;
        }

        var client = _httpClientFactory.CreateClient("smartsheet-feedback");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"sheets/{Uri.EscapeDataString(_options.SmartsheetSheetId!)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SmartsheetApiToken);

        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Smartsheet metadata fetch failed with status {(int)response.StatusCode}.");
        }

        foreach (var (title, id) in ReadColumnIds(body))
        {
            columnIds.TryAdd(title, id);
        }

        return columnIds;
    }

    private static IReadOnlyDictionary<string, long> ReadColumnIds(string body)
    {
        var columns = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body))
        {
            return columns;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("columns", out var columnsElement)
                || columnsElement.ValueKind != JsonValueKind.Array)
            {
                return columns;
            }

            foreach (var column in columnsElement.EnumerateArray())
            {
                if (!column.TryGetProperty("title", out var titleElement)
                    || !column.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                var title = titleElement.GetString();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var id = idElement.ValueKind == JsonValueKind.String
                    ? long.TryParse(idElement.GetString(), out var parsed) ? parsed : 0
                    : idElement.GetInt64();
                if (id > 0)
                {
                    columns[title.Trim()] = id;
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        return columns;
    }

    private static string? TryReadRowId(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array
                || result.GetArrayLength() == 0)
            {
                return null;
            }

            var first = result[0];
            if (first.TryGetProperty("id", out var id))
            {
                return id.ValueKind == JsonValueKind.String ? id.GetString() : id.ToString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
