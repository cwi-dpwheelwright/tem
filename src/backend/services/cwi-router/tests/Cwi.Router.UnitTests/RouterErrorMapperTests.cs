using System.Threading;
using Cwi.Router.Application.Handlers;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class RouterErrorMapperTests
{
    [Fact]
    public void FromException_TaskCanceled_MapsToGatewayTimeout()
    {
        var response = RouterErrorMapper.FromException(new TaskCanceledException());

        response.StatusCode.Should().Be(504);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("dependency_unavailable");
    }

    [Fact]
    public void FromException_OtherException_MapsToServiceUnavailable()
    {
        var response = RouterErrorMapper.FromException(new InvalidOperationException());

        response.StatusCode.Should().Be(503);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("dependency_unavailable");
    }

    [Fact]
    public void FromUpstreamStatus_ServerError_MapsToServiceUnavailable()
    {
        var response = RouterErrorMapper.FromUpstreamStatus(500);

        response.StatusCode.Should().Be(503);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be("dependency_unavailable");
    }
}
