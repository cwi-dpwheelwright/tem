using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests;

public sealed class DomainServiceBaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess_WhenActionSucceeds()
    {
        var service = new TestService();

        var response = await service.RunAsync<int>((Func<Task<int>>)(() => Task.FromResult(42)));

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Data.Should().Be(42);
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFailure_WhenActionThrows()
    {
        var service = new TestService();

        var response = await service.RunAsync<int>((Func<Task<int>>)(() => throw new InvalidOperationException("boom")));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(500);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteAsync_PassesThrough_ServiceResponse()
    {
        var service = new TestService();
        var expected = ServiceResponse<string>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, "bad"),
            statusCode: 400,
            message: "bad request");

        var response = await service.RunAsync(() => Task.FromResult(expected));

        response.Should().BeSameAs(expected);
    }

    private sealed class TestService : DomainServiceBase
    {
        public Task<ServiceResponse<T>> RunAsync<T>(Func<Task<T>> action)
        {
            return ExecuteAsync(action);
        }

        public Task<ServiceResponse<T>> RunAsync<T>(Func<Task<ServiceResponse<T>>> action)
        {
            return ExecuteAsync(action);
        }
    }
}
