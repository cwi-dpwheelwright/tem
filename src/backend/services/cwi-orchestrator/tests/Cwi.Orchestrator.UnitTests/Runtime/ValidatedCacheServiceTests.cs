#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class ValidatedCacheServiceTests
{
    [Fact]
    public void Evaluate_returns_eligible_for_valid_fresh_policy_compatible_entry()
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());
        var entry = CreateRecord();

        var result = service.Evaluate(entry, CreateContext());

        result.State.Should().Be(ValidatedCacheStates.Eligible);
        result.Reasons.Should().Contain("cache_entry_validated");
    }

    [Fact]
    public void Evaluate_rejects_user_scoped_cache_when_user_hash_differs()
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());
        var entry = CreateRecord(scope: "user", userIdHash: "user-a");

        var result = service.Evaluate(entry, CreateContext(userIdHash: "user-b"));

        result.State.Should().Be(ValidatedCacheStates.Invalid);
        result.Reasons.Should().Contain("user_scope_mismatch");
    }

    [Fact]
    public void Evaluate_returns_stale_when_freshness_expired()
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());
        var entry = CreateRecord(freshUntilUtc: DateTimeOffset.Parse("2026-05-06T10:00:00Z"));

        var result = service.Evaluate(entry, CreateContext(nowUtc: DateTimeOffset.Parse("2026-05-06T12:00:00Z")));

        result.State.Should().Be(ValidatedCacheStates.Stale);
        result.Reasons.Should().Contain("freshness_expired");
    }

    [Theory]
    [InlineData("different_policy", "policy_mode_changed")]
    [InlineData("different_pack", "pack_hash_changed")]
    public void Evaluate_requires_refresh_when_policy_or_pack_context_changed(string mode, string expectedReason)
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());
        var entry = CreateRecord();
        var context = mode switch
        {
            "different_policy" => CreateContext(policyMode: "strict"),
            "different_pack" => CreateContext(packHash: "pack-hash-2"),
            _ => CreateContext()
        };

        var result = service.Evaluate(entry, context);

        result.State.Should().Be(ValidatedCacheStates.RequiresRefresh);
        result.Reasons.Should().Contain(expectedReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public void Evaluate_invalidates_entries_without_source_proof(string sourceProofJson)
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());
        var entry = CreateRecord(sourceProofJson: sourceProofJson);

        var result = service.Evaluate(entry, CreateContext());

        result.State.Should().Be(ValidatedCacheStates.Invalid);
        result.Reasons.Should().Contain("source_proof_invalid");
    }

    [Fact]
    public async Task EvaluateAsync_returns_requires_refresh_when_entry_is_missing()
    {
        var service = new ValidatedCacheService(new FakeValidatedCacheRepository());

        var result = await service.EvaluateAsync(
            "tenant-1",
            "missing-key",
            CreateContext(),
            CancellationToken.None);

        result.State.Should().Be(ValidatedCacheStates.RequiresRefresh);
        result.Reasons.Should().Contain("cache_entry_missing");
    }

    private static ValidatedCacheRecord CreateRecord(
        string scope = "tenant",
        string? userIdHash = null,
        string sourceProofJson = "{\"source_id\":\"source-1\",\"trace_id\":\"trace-1\"}",
        DateTimeOffset? freshUntilUtc = null)
    {
        return new ValidatedCacheRecord
        {
            Id = Guid.NewGuid(),
            CacheKey = "cache-key-1",
            TenantId = "tenant-1",
            Scope = scope,
            UserIdHash = userIdHash,
            SourceProofJson = sourceProofJson,
            TraceId = "trace-1",
            FreshUntilUtc = freshUntilUtc ?? DateTimeOffset.Parse("2026-05-06T13:00:00Z"),
            PolicyMode = "observe",
            PolicyVersion = "policy-v1",
            PackHash = "pack-hash-1",
            ValidationStatus = "validated",
            CriticStatus = "pass",
            CreatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            UpdatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z")
        };
    }

    private static ValidatedCacheLookupContextDto CreateContext(
        string? userIdHash = null,
        string policyMode = "observe",
        string packHash = "pack-hash-1",
        DateTimeOffset? nowUtc = null)
    {
        return new ValidatedCacheLookupContextDto
        {
            TenantId = "tenant-1",
            UserIdHash = userIdHash,
            PolicyMode = policyMode,
            PolicyVersion = "policy-v1",
            PackHash = packHash,
            NowUtc = nowUtc ?? DateTimeOffset.Parse("2026-05-06T12:00:00Z")
        };
    }

    private sealed class FakeValidatedCacheRepository : IValidatedCacheRepository
    {
        private readonly List<ValidatedCacheRecord> _records = new();

        public Task<ValidatedCacheRecord?> GetAsync(
            string tenantId,
            string cacheKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.FirstOrDefault(record =>
                record.TenantId == tenantId
                && record.CacheKey == cacheKey));
        }

        public Task UpsertAsync(
            ValidatedCacheRecord record,
            CancellationToken cancellationToken)
        {
            _records.RemoveAll(item => item.TenantId == record.TenantId && item.CacheKey == record.CacheKey);
            _records.Add(record);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
