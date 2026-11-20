using System.Net;
using System.Net.Http.Json;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class ActionControllerTests
{
    [Fact]
    public async Task Action_ReturnsServiceUnavailable_WhenDependenciesDown()
    {
        var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new ActionRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PolicyMode = "approve",
            Action = new ActionDescriptorDto
            {
                Type = "risk_demo",
                Payload = new Dictionary<string, object?>
                {
                    ["value"] = "test"
                }
            },
            PackHash = "pack-hash",
            PackVersion = "1.0.0",
            ModelId = "model-id",
            PolicyVersion = "v1"
        };

        var response = await client.PostAsJsonAsync("/api/action", request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DomainError>();
        Assert.NotNull(payload);
        Assert.Equal(ErrorCodes.DependencyUnavailable, payload!.Code);
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
                    services.AddSingleton(new AuthOptions { AuthRequired = false });
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
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "1");
    }
}
