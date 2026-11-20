using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cwi.Coordination.IntegrationTests;

public sealed class ReadyEndpointTests
{
    [Fact]
    public async Task Ready_Returns_503_When_Dependency_Down()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dependencies:Urls:0"] = "http://localhost:59999/health"
                    });
                });
            });

        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns_200_When_No_Dependencies_Configured()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [DockerFact]
    public async Task Postgres_Testcontainer_Starts()
    {
        await using var container = new ContainerBuilder("postgres:16-alpine")
            .WithEnvironment("POSTGRES_DB", "coordination")
            .WithEnvironment("POSTGRES_USER", "cwi")
            .WithEnvironment("POSTGRES_PASSWORD", "cwi")
            .WithPortBinding(5432, true)
            .Build();

        await container.StartAsync();
        var port = container.GetMappedPublicPort(5432);
        Assert.True(port > 0);
    }
}
