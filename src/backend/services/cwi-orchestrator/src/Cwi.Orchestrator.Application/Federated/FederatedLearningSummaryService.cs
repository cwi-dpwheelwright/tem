#nullable enable
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Federated;

public sealed class FederatedLearningSummaryService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SafeTokenPattern = new("^[A-Za-z0-9_.:-]{1,64}$", RegexOptions.Compiled);
    private static readonly string[] UnsafeMetadataKeys =
    {
        "raw",
        "raw_text",
        "text",
        "body",
        "prompt",
        "answer",
        "message",
        "conversation",
        "transcript",
        "file_content",
        "document",
        "email",
        "note"
    };

    private readonly FederatedLearningSummaryOptions _options;
    private readonly SelectiveEnforcementOptions _enforcementOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public FederatedLearningSummaryService(
        FederatedLearningSummaryOptions options,
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

    public Task<ServiceResponse<FederatedLearningSummaryResponseDto>> SubmitAsync(
        FederatedLearningSummaryRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var reasons = new List<string>();
            var traceId = string.IsNullOrWhiteSpace(request.TraceId)
                ? $"federated-summary-{Guid.NewGuid():N}"
                : request.TraceId!.Trim();
            var recordedUtc = DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return ServiceResponse<FederatedLearningSummaryResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                    statusCode: 400,
                    message: "tenant_id is required.");
            }

            if (_enforcementOptions.Resolve(SelectiveEnforcementFeature.FederatedSummaries) == SelectiveEnforcementMode.Disabled)
            {
                reasons.Add("federated_summary_feature_disabled");
                var disabledResponse = CreateResponse(
                    request,
                    traceId,
                    "disabled",
                    approvedForExport: false,
                    teacherSubmissionStatus: "not_sent",
                    teacherStatusCode: null,
                    groups: Array.Empty<FederatedLearningSummaryGroupDto>(),
                    reasons,
                    recordedUtc);
                await RecordLedgerEventAsync(disabledResponse, request, "federated_learning.summary_disabled", cancellationToken)
                    .ConfigureAwait(false);
                return ServiceResponse<FederatedLearningSummaryResponseDto>.Success(disabledResponse);
            }

            if (request.Observations.Count > _options.MaxObservations)
            {
                reasons.Add("observation_count_exceeds_limit");
            }

            if (ContainsUnsafeMetadata(request.Observations))
            {
                reasons.Add("raw_or_private_metadata_detected");
            }

            if (reasons.Count > 0)
            {
                var rejectedResponse = CreateResponse(
                    request,
                    traceId,
                    "rejected",
                    approvedForExport: false,
                    teacherSubmissionStatus: "not_sent",
                    teacherStatusCode: null,
                    groups: Array.Empty<FederatedLearningSummaryGroupDto>(),
                    reasons,
                    recordedUtc);
                await RecordLedgerEventAsync(rejectedResponse, request, "federated_learning.summary_rejected", cancellationToken)
                    .ConfigureAwait(false);
                return ServiceResponse<FederatedLearningSummaryResponseDto>.Success(rejectedResponse, statusCode: 202);
            }

            var groups = BuildSummaryGroups(request.Observations);
            if (groups.Count == 0)
            {
                reasons.Add("no_exportable_summary_groups");
            }

            var policyAllowsExport = IsApprovedPolicy(request.PolicyStatus);
            if (!policyAllowsExport)
            {
                reasons.Add("policy_status_not_approved");
            }

            var teacherConfigured = _options.TeacherSubmissionEnabled
                                    && !string.IsNullOrWhiteSpace(_options.TeacherBaseUrl);
            if (!teacherConfigured)
            {
                reasons.Add("teacher_submission_not_configured");
            }

            var approvedForExport = policyAllowsExport && teacherConfigured && groups.Count > 0;
            var teacherStatus = approvedForExport ? "pending" : "not_sent";
            int? teacherStatusCode = null;

            var response = CreateResponse(
                request,
                traceId,
                approvedForExport ? "approved" : "observed",
                approvedForExport,
                teacherStatus,
                teacherStatusCode,
                groups,
                reasons,
                recordedUtc);

            if (approvedForExport)
            {
                var submission = await SubmitToTeacherAsync(request, response, cancellationToken)
                    .ConfigureAwait(false);
                response = response.WithSubmission(submission.Status, submission.StatusCode);
            }

            await RecordLedgerEventAsync(response, request, "federated_learning.summary_observed", cancellationToken)
                .ConfigureAwait(false);
            return ServiceResponse<FederatedLearningSummaryResponseDto>.Success(response);
        });
    }

    private static bool IsApprovedPolicy(string policyStatus)
    {
        return policyStatus.Trim().Equals("approved", StringComparison.OrdinalIgnoreCase)
               || policyStatus.Trim().Equals("allowed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsUnsafeMetadata(IReadOnlyList<FederatedLearningObservationDto> observations)
    {
        foreach (var observation in observations)
        {
            if (observation.Metadata is null)
            {
                continue;
            }

            foreach (var pair in observation.Metadata)
            {
                if (UnsafeMetadataKeys.Any(key => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                                                 || pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(pair.Value) && pair.Value.Length > 120)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<FederatedLearningSummaryGroupDto> BuildSummaryGroups(
        IReadOnlyList<FederatedLearningObservationDto> observations)
    {
        return observations
            .Select(observation => new SanitizedObservation(
                Category: SafeToken(observation.Category, "uncategorized"),
                Signal: SafeToken(observation.Signal, "general_signal"),
                Outcome: SafeToken(observation.Outcome, "observed"),
                Count: Math.Max(1, observation.Count),
                Confidence: Math.Clamp(observation.Confidence, 0.0d, 1.0d),
                Tags: observation.Tags
                    .Select(tag => SafeToken(tag, string.Empty))
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(_options.MaxTagCount)
                    .ToArray()))
            .GroupBy(item => new { item.Category, item.Signal, item.Outcome })
            .Select(group =>
            {
                var count = group.Sum(item => item.Count);
                var weightedConfidence = group.Sum(item => item.Confidence * item.Count) / count;
                return new FederatedLearningSummaryGroupDto
                {
                    Category = group.Key.Category,
                    Signal = group.Key.Signal,
                    Outcome = group.Key.Outcome,
                    Count = count,
                    AverageConfidence = Math.Round(weightedConfidence, 4),
                    Tags = group.SelectMany(item => item.Tags)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(_options.MaxTagCount)
                        .ToArray()
                };
            })
            .OrderBy(group => group.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Signal, StringComparer.Ordinal)
            .ThenBy(group => group.Outcome, StringComparer.Ordinal)
            .ToList();
    }

    private static string SafeToken(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return SafeTokenPattern.IsMatch(trimmed) ? trimmed.ToLowerInvariant() : fallback;
    }

    private FederatedLearningSummaryResponseDto CreateResponse(
        FederatedLearningSummaryRequestDto request,
        string traceId,
        string status,
        bool approvedForExport,
        string teacherSubmissionStatus,
        int? teacherStatusCode,
        IReadOnlyList<FederatedLearningSummaryGroupDto> groups,
        IReadOnlyList<string> reasons,
        DateTimeOffset recordedUtc)
    {
        var summaryId = ComputeSummaryId(request, traceId, groups);
        return new FederatedLearningSummaryResponseDto
        {
            SummaryId = summaryId,
            Status = status,
            ApprovedForExport = approvedForExport,
            TeacherSubmissionStatus = teacherSubmissionStatus,
            TeacherStatusCode = teacherStatusCode,
            RawContentExported = false,
            ObservationCount = request.Observations.Count,
            SummaryGroupCount = groups.Count,
            SummaryGroups = groups,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            TraceId = traceId,
            RecordedUtc = recordedUtc
        };
    }

    private async Task<TeacherSubmissionResult> SubmitToTeacherAsync(
        FederatedLearningSummaryRequestDto request,
        FederatedLearningSummaryResponseDto response,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            schema_version = "federated_learning_summary.v1",
            summary_id = response.SummaryId,
            tenant_id = request.TenantId.Trim(),
            user_id_hash = string.IsNullOrWhiteSpace(request.UserIdHash) ? null : request.UserIdHash.Trim(),
            node_id = string.IsNullOrWhiteSpace(request.NodeId) ? null : request.NodeId.Trim(),
            trace_id = response.TraceId,
            policy_status = request.PolicyStatus.Trim(),
            source_component = request.SourceComponent.Trim(),
            generated_utc = response.RecordedUtc,
            raw_content_exported = false,
            summary_groups = response.SummaryGroups,
            provenance = new
            {
                source_hash = ResolveSourceHash(request, response.SummaryGroups),
                source_id = request.Provenance?.SourceId,
                generated_utc = request.Provenance?.GeneratedUtc ?? response.RecordedUtc
            }
        };
        var client = _httpClientFactory.CreateClient("teacher-node");
        var baseUri = new Uri(_options.TeacherBaseUrl.TrimEnd('/') + "/");
        var target = new Uri(baseUri, _options.TeacherSummaryPath.TrimStart('/'));
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var httpResponse = await client.PostAsync(target, content, cancellationToken).ConfigureAwait(false);
        return new TeacherSubmissionResult(
            httpResponse.IsSuccessStatusCode ? "submitted" : "failed",
            (int)httpResponse.StatusCode);
    }

    private async Task RecordLedgerEventAsync(
        FederatedLearningSummaryResponseDto response,
        FederatedLearningSummaryRequestDto request,
        string eventType,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            event_name = eventType,
            summary_id = response.SummaryId,
            tenant_id = request.TenantId,
            node_id = request.NodeId,
            trace_id = response.TraceId,
            status = response.Status,
            approved_for_export = response.ApprovedForExport,
            teacher_submission_status = response.TeacherSubmissionStatus,
            teacher_status_code = response.TeacherStatusCode,
            raw_content_exported = false,
            observation_count = response.ObservationCount,
            summary_group_count = response.SummaryGroupCount,
            reasons = response.Reasons,
            source_component = request.SourceComponent,
            source_hash = ResolveSourceHash(request, response.SummaryGroups),
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

    private static string ComputeSummaryId(
        FederatedLearningSummaryRequestDto request,
        string traceId,
        IReadOnlyList<FederatedLearningSummaryGroupDto> groups)
    {
        var source = new
        {
            tenant_id = request.TenantId,
            user_id_hash = request.UserIdHash,
            node_id = request.NodeId,
            trace_id = traceId,
            policy_status = request.PolicyStatus,
            groups
        };
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return "fed-sum-" + Sha256(json)[..32];
    }

    private static string ResolveSourceHash(
        FederatedLearningSummaryRequestDto request,
        IReadOnlyList<FederatedLearningSummaryGroupDto> groups)
    {
        if (!string.IsNullOrWhiteSpace(request.Provenance?.SourceHash))
        {
            return request.Provenance!.SourceHash!.Trim();
        }

        return Sha256(JsonSerializer.Serialize(groups, JsonOptions));
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record SanitizedObservation(
        string Category,
        string Signal,
        string Outcome,
        int Count,
        double Confidence,
        IReadOnlyList<string> Tags);

    private sealed record TeacherSubmissionResult(string Status, int StatusCode);
}

internal static class FederatedLearningSummaryResponseExtensions
{
    public static FederatedLearningSummaryResponseDto WithSubmission(
        this FederatedLearningSummaryResponseDto response,
        string teacherSubmissionStatus,
        int teacherStatusCode)
    {
        return new FederatedLearningSummaryResponseDto
        {
            SummaryId = response.SummaryId,
            Status = response.Status,
            ApprovedForExport = response.ApprovedForExport,
            TeacherSubmissionStatus = teacherSubmissionStatus,
            TeacherStatusCode = teacherStatusCode,
            RawContentExported = response.RawContentExported,
            ObservationCount = response.ObservationCount,
            SummaryGroupCount = response.SummaryGroupCount,
            SummaryGroups = response.SummaryGroups,
            Reasons = response.Reasons,
            TraceId = response.TraceId,
            RecordedUtc = response.RecordedUtc
        };
    }
}
#nullable disable
