using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class RedisContainerTests : IAsyncLifetime
{
    private readonly Lazy<IContainer> _redis = new(() => new ContainerBuilder()
        .WithImage("redis:7.2-alpine")
        .WithPortBinding(6379, true)
        .Build());

    public async Task InitializeAsync()
    {
        await _redis.Value.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.Value.DisposeAsync();
    }

    [Fact]
    public void RedisContainer_ShouldExposeMappedPort()
    {
        var port = _redis.Value.GetMappedPublicPort(6379);
        port.Should().BeGreaterThan(0);
    }
}
