using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class AskControllerTests
{
    [Theory]
    [InlineData("/api/ask")]
    [InlineData("/mcp/ask")]
    public async Task Ask_ReturnsUnauthorized_WhenJwtMissing(string path)
    {
        var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new AskRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "What is the system status?",
            Pack = new PackRef { Name = "enterprise" },
            Context = new AskContext { Mode = "observe", TopK = 3 }
        };

        var response = await client.PostAsJsonAsync(path, request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        ConfigureEnvironment();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(AuthOptions));
                    services.AddSingleton(new AuthOptions { AuthRequired = true });
                });
            });
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("REGISTRY", "registry.example.com");
        Environment.SetEnvironmentVariable("CWI_ORCHESTRATOR_TAG", "test");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("PACKS_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_DB_CONNECTION", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("CWI_GRAPH_DB_DSN", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "1");
    }
}
