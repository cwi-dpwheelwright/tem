#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class ValidatedCacheService
{
    private readonly IValidatedCacheRepository _repository;

    public ValidatedCacheService(IValidatedCacheRepository repository)
    {
        _repository = repository;
    }

    public async Task UpsertAsync(
        ValidatedCacheEntryDto entry,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(
            new ValidatedCacheRecord
            {
                Id = Guid.NewGuid(),
                CacheKey = entry.CacheKey.Trim(),
                TenantId = entry.TenantId.Trim(),
                Scope = NormalizeScope(entry.Scope),
                UserIdHash = NormalizeOptional(entry.UserIdHash),
                SourceProofJson = NormalizeSourceProof(entry.SourceProofJson),
                TraceId = entry.TraceId.Trim(),
                FreshUntilUtc = entry.FreshUntilUtc.ToUniversalTime(),
                PolicyMode = entry.PolicyMode.Trim(),
                PolicyVersion = NormalizeOptional(entry.PolicyVersion),
                PackHash = entry.PackHash.Trim(),
                ValidationStatus = NormalizeStatus(entry.ValidationStatus),
                CriticStatus = NormalizeOptional(entry.CriticStatus),
                PayloadHash = NormalizeOptional(entry.PayloadHash),
                CreatedUtc = now,
                UpdatedUtc = now
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidatedCacheEvaluationDto> EvaluateAsync(
        string tenantId,
        string cacheKey,
        ValidatedCacheLookupContextDto context,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(tenantId.Trim(), cacheKey.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return new ValidatedCacheEvaluationDto
            {
                CacheKey = cacheKey,
                State = ValidatedCacheStates.RequiresRefresh,
                Reasons = new[] { "cache_entry_missing" }
            };
        }

        return Evaluate(record, context);
    }

    public ValidatedCacheEvaluationDto Evaluate(
        ValidatedCacheRecord record,
        ValidatedCacheLookupContextDto context)
    {
        var invalidReasons = new List<string>();
        var refreshReasons = new List<string>();

        if (!string.Equals(record.TenantId, context.TenantId, StringComparison.Ordinal))
        {
            invalidReasons.Add("tenant_mismatch");
        }

        if (string.IsNullOrWhiteSpace(record.CacheKey))
        {
            invalidReasons.Add("cache_key_missing");
        }

        if (string.IsNullOrWhiteSpace(record.TraceId))
        {
            invalidReasons.Add("trace_id_missing");
        }

        if (string.IsNullOrWhiteSpace(record.PackHash))
        {
            invalidReasons.Add("pack_hash_missing");
        }

        if (string.IsNullOrWhiteSpace(record.SourceProofJson) || !IsJsonObject(record.SourceProofJson))
        {
            invalidReasons.Add("source_proof_invalid");
        }

        if (!string.Equals(record.ValidationStatus, "validated", StringComparison.OrdinalIgnoreCase))
        {
            invalidReasons.Add("validation_status_not_validated");
        }

        if (IsUserScoped(record) && !string.Equals(record.UserIdHash, context.UserIdHash, StringComparison.Ordinal))
        {
            invalidReasons.Add("user_scope_mismatch");
        }

        if (IsCriticFailure(record.CriticStatus))
        {
            invalidReasons.Add("critic_status_failed");
        }

        if (invalidReasons.Count > 0)
        {
            return Build(record, ValidatedCacheStates.Invalid, invalidReasons);
        }

        if (!string.Equals(record.PolicyMode, context.PolicyMode, StringComparison.OrdinalIgnoreCase))
        {
            refreshReasons.Add("policy_mode_changed");
        }

        if (!string.Equals(record.PolicyVersion, context.PolicyVersion, StringComparison.Ordinal))
        {
            refreshReasons.Add("policy_version_changed");
        }

        if (!string.Equals(record.PackHash, context.PackHash, StringComparison.Ordinal))
        {
            refreshReasons.Add("pack_hash_changed");
        }

        if (IsCriticWarning(record.CriticStatus))
        {
            refreshReasons.Add("critic_warning_present");
        }

        if (refreshReasons.Count > 0)
        {
            return Build(record, ValidatedCacheStates.RequiresRefresh, refreshReasons);
        }

        if (record.FreshUntilUtc <= context.NowUtc.ToUniversalTime())
        {
            return Build(record, ValidatedCacheStates.Stale, new[] { "freshness_expired" });
        }

        return Build(record, ValidatedCacheStates.Eligible, new[] { "cache_entry_validated" });
    }

    private static ValidatedCacheEvaluationDto Build(
        ValidatedCacheRecord record,
        string state,
        IReadOnlyList<string> reasons)
    {
        return new ValidatedCacheEvaluationDto
        {
            CacheKey = record.CacheKey,
            State = state,
            Reasons = reasons,
            FreshUntilUtc = record.FreshUntilUtc
        };
    }

    private static bool IsUserScoped(ValidatedCacheRecord record)
    {
        return string.Equals(record.Scope, "user", StringComparison.OrdinalIgnoreCase)
               || !string.IsNullOrWhiteSpace(record.UserIdHash);
    }

    private static bool IsCriticFailure(string? criticStatus)
    {
        return string.Equals(criticStatus, "fail", StringComparison.OrdinalIgnoreCase)
               || string.Equals(criticStatus, "failed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(criticStatus, "blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCriticWarning(string? criticStatus)
    {
        return string.Equals(criticStatus, "warn", StringComparison.OrdinalIgnoreCase)
               || string.Equals(criticStatus, "warning", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonObject(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeSourceProof(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
    }

    private static string NormalizeScope(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "tenant" : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeStatus(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "pending" : value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
#nullable disable
