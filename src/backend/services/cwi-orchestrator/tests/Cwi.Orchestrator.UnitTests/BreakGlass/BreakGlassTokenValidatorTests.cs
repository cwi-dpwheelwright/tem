using System;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.BreakGlass;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.BreakGlass;

public class BreakGlassTokenValidatorTests
{
    [Fact]
    public async Task ValidateAsync_rejects_expired_token()
    {
        var repo = new FakeRepo
        {
            Token = new BreakGlassToken
            {
                TokenId = "token-1",
                Scope = "system",
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        };
        var validator = new BreakGlassTokenValidator(repo, new BreakGlassDenylistCache());

        var result = await validator.ValidateAsync("token-1", "system", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    private sealed class FakeRepo : IBreakGlassTokenRepository
    {
        public BreakGlassToken? Token { get; set; }

        public Task AddAsync(BreakGlassToken token, CancellationToken cancellationToken)
        {
            Token = token;
            return Task.CompletedTask;
        }

        public Task<BreakGlassToken?> GetByTokenIdAsync(string tokenId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Token);
        }

        public Task RevokeAsync(BreakGlassToken token, CancellationToken cancellationToken)
        {
            Token = token;
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
}
