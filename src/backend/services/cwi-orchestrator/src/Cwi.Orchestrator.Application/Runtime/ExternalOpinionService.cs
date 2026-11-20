#nullable enable
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class ExternalOpinionService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ExternalOpinionOptions _options;
    private readonly SelectiveEnforcementOptions _enforcementOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public ExternalOpinionService(
        ExternalOpinionOptions options,
        SelectiveEnforcementOptions enforcementOptions,
        IHttpClientFactory httpClientFactory,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _options = options;
        _enforcementOptions = enforcementOptions;
        _httpClientFactory = httpClientFactory;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<ExternalOpinionResponseDto>> EvaluateAsync(
        ExternalOpinionRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return ServiceResponse<ExternalOpinionResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                    statusCode: 400,
                    message: "tenant_id is required.");
            }

            var traceId = string.IsNullOrWhiteSpace(request.TraceId)
                ? $"external-opinion-{Guid.NewGuid():N}"
                : request.TraceId.Trim();
            var recordedUtc = DateTimeOffset.UtcNow;
            var reasons = new List<string>();

            if (_enforcementOptions.Resolve(SelectiveEnforcementFeature.ExternalOpinion) == SelectiveEnforcementMode.Disabled)
            {
                reasons.Add("external_opinion_feature_disabled");
                var disabled = CreateResponse("disabled", "not_sent", null, Array.Empty<ExternalOpinionFindingDto>(), reasons, traceId, recordedUtc);
                await RecordLedgerEventAsync(disabled, request, "external_opinion.disabled", cancellationToken).ConfigureAwait(false);
                return ServiceResponse<ExternalOpinionResponseDto>.Success(disabled);
            }

            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.BaseUrl))
            {
                reasons.Add("external_opinion_not_configured");
            }

            if (!IsApprovedPolicy(request.PolicyStatus))
            {
                reasons.Add("policy_status_not_approved");
            }

            if (string.IsNullOrWhiteSpace(request.PromptSummary))
            {
                reasons.Add("prompt_summary_required");
            }

            var canSubmit = reasons.Count == 0;
            if (!canSubmit)
            {
                var observed = CreateResponse("observed", "not_sent", null, Array.Empty<ExternalOpinionFindingDto>(), reasons, traceId, recordedUtc);
                await RecordLedgerEventAsync(observed, request, "external_opinion.observed", cancellationToken).ConfigureAwait(false);
                return ServiceResponse<ExternalOpinionResponseDto>.Success(observed);
            }

            var submission = await SubmitExternalAsync(request, traceId, cancellationToken).ConfigureAwait(false);
            var response = CreateResponse(
                "observed",
                submission.Status,
                submission.StatusCode,
                submission.Findings,
                reasons,
                traceId,
                recordedUtc);
            await RecordLedgerEventAsync(response, request, "external_opinion.observed", cancellationToken).ConfigureAwait(false);
            return ServiceResponse<ExternalOpinionResponseDto>.Success(response);
        });
    }

    private static bool IsApprovedPolicy(string policyStatus)
    {
        return policyStatus.Trim().Equals("approved", StringComparison.OrdinalIgnoreCase)
               || policyStatus.Trim().Equals("allowed", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ExternalSubmissionResult> SubmitExternalAsync(
        ExternalOpinionRequestDto request,
        string traceId,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            schema_version = "external_opinion_request.v1",
            tenant_id = request.TenantId.Trim(),
            trace_id = traceId,
            purpose = SafeToken(request.Purpose, "response_quality_review"),
            prompt_summary = Truncate(request.PromptSummary.Trim(), _options.MaxPromptSummaryChars),
            context_summary = string.IsNullOrWhiteSpace(request.ContextSummary)
                ? null
                : Truncate(request.ContextSummary!.Trim(), _options.MaxContextSummaryChars),
            governed_answer_hash = request.GovernedAnswerHash,
            evidence_hashes = request.EvidenceHashes
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Select(hash => hash.Trim())
                .Take(25)
                .ToArray(),
            advisory_only = true
        };
        var client = _httpClientFactory.CreateClient("external-opinion");
        var baseUri = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        var target = new Uri(baseUri, _options.EvaluationPath.TrimStart('/'));
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var httpResponse = await client.PostAsync(target, content, cancellationToken).ConfigureAwait(false);
        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var findings = httpResponse.IsSuccessStatusCode
            ? ParseFindings(body)
            : new[]
            {
                new ExternalOpinionFindingDto
                {
                    Category = "external_dependency",
                    Severity = "warning",
                    Message = "External advisory evaluator returned a non-success status."
                }
            };
        return new ExternalSubmissionResult(
            httpResponse.IsSuccessStatusCode ? "submitted" : "failed",
            (int)httpResponse.StatusCode,
            findings);
    }

    private static IReadOnlyList<ExternalOpinionFindingDto> ParseFindings(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<ExternalOpinionFindingDto>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("findings", out var findings)
                || findings.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ExternalOpinionFindingDto>();
            }

            return findings.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => new ExternalOpinionFindingDto
                {
                    Category = SafeToken(ReadString(item, "category"), "external_opinion"),
                    Severity = SafeToken(ReadString(item, "severity"), "info"),
                    Message = Truncate(ReadString(item, "message"), 300)
                })
                .Where(finding => !string.IsNullOrWhiteSpace(finding.Message))
                .Take(20)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<ExternalOpinionFindingDto>();
        }
    }

    private ExternalOpinionResponseDto CreateResponse(
        string status,
        string externalSubmissionStatus,
        int? externalStatusCode,
        IReadOnlyList<ExternalOpinionFindingDto> findings,
        IReadOnlyList<string> reasons,
        string traceId,
        DateTimeOffset recordedUtc)
    {
        return new ExternalOpinionResponseDto
        {
            Status = status,
            AdvisoryOnly = true,
            AppliedToAnswer = false,
            ExternalSubmissionStatus = externalSubmissionStatus,
            ExternalStatusCode = externalStatusCode,
            Findings = findings,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            TraceId = traceId,
            RecordedUtc = recordedUtc
        };
    }

    private async Task RecordLedgerEventAsync(
        ExternalOpinionResponseDto response,
        ExternalOpinionRequestDto request,
        string eventType,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            event_name = eventType,
            tenant_id = request.TenantId,
            trace_id = response.TraceId,
            status = response.Status,
            advisory_only = true,
            applied_to_answer = false,
            external_submission_status = response.ExternalSubmissionStatus,
            external_status_code = response.ExternalStatusCode,
            findings_count = response.Findings.Count,
            reasons = response.Reasons,
            prompt_summary_hash = Sha256(request.PromptSummary ?? string.Empty),
            governed_answer_hash = request.GovernedAnswerHash,
            evidence_hashes = request.EvidenceHashes,
            recorded_utc = response.RecordedUtc
        };
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(response.TraceId, cancellationToken)
            .ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            response.TraceId,
            eventType,
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string SafeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            return fallback;
        }

        return trimmed.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or ':')
            ? trimmed.ToLowerInvariant()
            : fallback;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ExternalSubmissionResult(
        string Status,
        int StatusCode,
        IReadOnlyList<ExternalOpinionFindingDto> Findings);
}
#nullable disable
