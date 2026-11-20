using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class ActivationStatusServiceTests
{
    [Fact]
    public async Task GetStatusAsync_Returns_Unknown_When_Missing()
    {
        var repo = new InMemoryMembershipRepository();
        var service = new ActivationStatusService(repo);

        var response = await service.GetStatusAsync("node-1", CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.Status.Should().Be("unknown");
    }

    [Fact]
    public async Task GetStatusAsync_Returns_Active_For_Member()
    {
        var repo = new InMemoryMembershipRepository();
        var service = new ActivationStatusService(repo);
        await repo.AddAsync(new Member
        {
            NodeId = "node-1",
            Status = MembershipStatus.Active
        }, CancellationToken.None);

        var response = await service.GetStatusAsync("node-1", CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.Status.Should().Be("active");
    }

    [Fact]
    public async Task GetStatusAsync_Returns_Revoked_With_Timestamp()
    {
        var repo = new InMemoryMembershipRepository();
        var service = new ActivationStatusService(repo);
        var revokedUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        await repo.AddAsync(new Member
        {
            NodeId = "node-1",
            Status = MembershipStatus.Revoked,
            RevokedUtc = revokedUtc
        }, CancellationToken.None);

        var response = await service.GetStatusAsync("node-1", CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.Status.Should().Be("revoked");
        response.Data?.RevokedUtc.Should().Be(revokedUtc);
    }

    [Fact]
    public async Task GetStatusAsync_Rejects_Empty_NodeId()
    {
        var repo = new InMemoryMembershipRepository();
        var service = new ActivationStatusService(repo);

        var response = await service.GetStatusAsync("", CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }
}
