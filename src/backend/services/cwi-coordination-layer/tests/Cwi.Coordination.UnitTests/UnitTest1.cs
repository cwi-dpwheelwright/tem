using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Repositories;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.Application.Pipeline;
using Cwi.Coordination.DataAccess;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class MembershipServiceTests
{
    [Fact]
    public async Task EnrollAsync_Returns_InvalidRequest_When_NodeIdMissing()
    {
        var repo = new InMemoryMembershipRepository();
        var pipeline = new MembershipPipeline(new IPipelineHandler<EnrollmentRequest, EnrollmentResponse>[]
        {
            new MembershipValidationHandler()
        });
        var service = new MembershipService(repo, pipeline);

        var response = await service.EnrollAsync(new EnrollmentRequest
        {
            NodeId = "",
            CertificateFingerprint = "abc"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Error?.Code.Should().Be(ErrorCodes.InvalidRequest);
        response.StatusCode.Should().Be(400);
    }
}

public sealed class RoundServiceTests
{
    [Fact]
    public async Task CreateAsync_Returns_InvalidRequest_When_NameMissing()
    {
        var repo = new InMemoryRoundRepository();
        var ledgerRepo = new InMemoryLedgerRepository();
        var ledger = new LedgerService(ledgerRepo);
        var service = new RoundService(repo, ledger);

        var response = await service.CreateAsync(new RoundCreateRequest { Name = "" }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Error?.Code.Should().Be(ErrorCodes.InvalidRequest);
        response.StatusCode.Should().Be(400);
    }
}
