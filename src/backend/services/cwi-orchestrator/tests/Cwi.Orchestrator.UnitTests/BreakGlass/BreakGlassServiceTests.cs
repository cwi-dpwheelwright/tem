using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.BreakGlass;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Cwi.Orchestrator.Application.Provenance;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.BreakGlass;

public class BreakGlassServiceTests
{
    [Fact]
    public async Task IssueAsync_returns_token()
    {
        var policy = new BreakGlassPolicy
        {
            AllowedRoles = new[] { "admin" },
            AllowedScopes = new[] { "system" },
            MaxDurationMinutes = 30,
            RequireReason = true
        };
        var validator = new BreakGlassPolicyValidator(policy);
        var repo = new FakeRepo();
        var ledger = new FakeLedgerRepository();
        var provenance = new BreakGlassProvenanceRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider()));
        var denylist = new BreakGlassDenylistCache();
        var notifier = new FakeNotifier();
        var service = new BreakGlassService(validator, repo, provenance, denylist, notifier);

        var result = await service.IssueAsync(new BreakGlassIssueRequestDto
        {
            TenantId = "tenant",
            UserId = "user",
            Role = "admin",
            Scope = "system",
            DurationMinutes = 10,
            Reason = "urgent"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        repo.LastToken.Should().NotBeNull();
        notifier.LastEvent.Should().Be("issued");
    }

    [Fact]
    public async Task RevokeAsync_marks_token_revoked_and_caches()
    {
        var policy = new BreakGlassPolicy
        {
            AllowedRoles = new[] { "admin" },
            AllowedScopes = new[] { "system" },
            MaxDurationMinutes = 30,
            RequireReason = true
        };
        var validator = new BreakGlassPolicyValidator(policy);
        var repo = new FakeRepo();
        var ledger = new FakeLedgerRepository();
        var denylist = new BreakGlassDenylistCache();
        var notifier = new FakeNotifier();
        var service = new BreakGlassService(
            validator,
            repo,
            new BreakGlassProvenanceRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider())),
            denylist,
            notifier);

        var token = new BreakGlassToken
        {
            Id = Guid.NewGuid(),
            TokenId = "token-1",
            TenantId = "tenant",
            UserId = "user",
            Role = "admin",
            Scope = "system",
            Reason = "urgent",
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        repo.Stored = token;

        var result = await service.RevokeAsync(new BreakGlassRevokeRequestDto
        {
            TokenId = "token-1"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        token.RevokedUtc.Should().NotBeNull();
        denylist.IsRevoked("token-1").Should().BeTrue();
        notifier.LastEvent.Should().Be("revoked");
        ledger.LastEvent.Should().NotBeNull();
        ledger.LastEvent!.EventType.Should().Be("break_glass.revoked");
    }

    [Fact]
    public async Task RevokeAsync_records_expiry_event_when_token_expired()
    {
        var policy = new BreakGlassPolicy
        {
            AllowedRoles = new[] { "admin" },
            AllowedScopes = new[] { "system" },
            MaxDurationMinutes = 30,
            RequireReason = true
        };
        var validator = new BreakGlassPolicyValidator(policy);
        var repo = new FakeRepo();
        var ledger = new FakeLedgerRepository();
        var denylist = new BreakGlassDenylistCache();
        var notifier = new FakeNotifier();
        var service = new BreakGlassService(
            validator,
            repo,
            new BreakGlassProvenanceRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider())),
            denylist,
            notifier);

        var token = new BreakGlassToken
        {
            Id = Guid.NewGuid(),
            TokenId = "token-2",
            TenantId = "tenant",
            UserId = "user",
            Role = "admin",
            Scope = "system",
            Reason = "urgent",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        repo.Stored = token;

        var result = await service.RevokeAsync(new BreakGlassRevokeRequestDto
        {
            TokenId = "token-2"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ledger.LastEvent.Should().NotBeNull();
        ledger.LastEvent!.EventType.Should().Be("break_glass.expired");
    }

    private sealed class FakeRepo : IBreakGlassTokenRepository
    {
        public BreakGlassToken? LastToken { get; private set; }
        public BreakGlassToken? Stored { get; set; }

        public Task AddAsync(BreakGlassToken token, CancellationToken cancellationToken)
        {
            LastToken = token;
            return Task.CompletedTask;
        }

        public Task<BreakGlassToken?> GetByTokenIdAsync(string tokenId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Stored);
        }

        public Task RevokeAsync(BreakGlassToken token, CancellationToken cancellationToken)
        {
            Stored = token;
            return Task.CompletedTask;
        }

        public Task<int> GetCountAsync(string tenantId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<BreakGlassToken>> GetByTenantAsync(string tenantId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BreakGlassToken>>(Array.Empty<BreakGlassToken>());
        }
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public LedgerEvent? LastEvent { get; private set; }

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            LastEvent = ledgerEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IBreakGlassNotifier
    {
        public string? LastEvent { get; private set; }

        public Task NotifyAsync(BreakGlassNotification notification, CancellationToken cancellationToken)
        {
            LastEvent = notification.EventType;
            return Task.CompletedTask;
        }
    }
}
