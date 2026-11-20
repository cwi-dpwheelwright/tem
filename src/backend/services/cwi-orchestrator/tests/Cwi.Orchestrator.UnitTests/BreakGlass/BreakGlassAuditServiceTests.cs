using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.BreakGlass;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.BreakGlass;

public class BreakGlassAuditServiceTests
{
    [Fact]
    public async Task QueryAsync_returns_page()
    {
        var repo = new FakeRepo();
        var service = new BreakGlassAuditService(repo);

        var result = await service.QueryAsync("tenant", 1, 10, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Total.Should().Be(1);
    }

    private sealed class FakeRepo : IBreakGlassTokenRepository
    {
        public Task AddAsync(BreakGlassToken token, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BreakGlassToken?> GetByTokenIdAsync(string tokenId, CancellationToken cancellationToken) => Task.FromResult<BreakGlassToken?>(null);
        public Task RevokeAsync(BreakGlassToken token, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> GetCountAsync(string tenantId, CancellationToken cancellationToken)
        {
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<BreakGlassToken>> GetByTenantAsync(string tenantId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BreakGlassToken>>(new[]
            {
                new BreakGlassToken { TokenId = "token-1", TenantId = tenantId }
            });
        }
    }
}
