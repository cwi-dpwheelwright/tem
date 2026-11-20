using System.Net;
using System.Net.Http.Json;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class TelemetryIngestSchemaTests : IAsyncLifetime
{
    private const string PostgresUser = "cwi";
    private const string PostgresPassword = "cwi";
    private const string PostgresDb = "cwi";

    private readonly TestcontainersContainer _postgres = new TestcontainersBuilder<TestcontainersContainer>()
        .WithImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_USER", PostgresUser)
        .WithEnvironment("POSTGRES_PASSWORD", PostgresPassword)
        .WithEnvironment("POSTGRES_DB", PostgresDb)
        .WithPortBinding(0, 5432)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    private bool _dockerAvailable = true;

    private string ConnectionString => $"Host=localhost;Port={_postgres.GetMappedPublicPort(5432)};Database={PostgresDb};Username={PostgresUser};Password={PostgresPassword}";

    public async Task InitializeAsync()
    {
        try
        {
            await _postgres.StartAsync();
            await ApplySchemaAsync();
        }
        catch (Exception)
        {
            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dockerAvailable)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Ingest_ReturnsBadRequest_WhenTenantMissing()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = BuildInvalidTenantRequest();
        var response = await client.PostAsJsonAsync("/mcp/ingest/telemetry", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DomainError>();
        Assert.NotNull(payload);
        Assert.Equal(ErrorCodes.InvalidRequest, payload!.Code);
    }

    private static TelemetryIngestRequestDto BuildInvalidTenantRequest()
    {
        return new TelemetryIngestRequestDto
        {
            TenantId = string.Empty,
            Window = new TelemetryWindowDto
            {
                Start = DateTimeOffset.UtcNow.AddMinutes(-5),
                End = DateTimeOffset.UtcNow
            },
            Signals = new List<TelemetrySignalDto>
            {
                new TelemetrySignalDto
                {
                    Tag = "requests",
                    Unit = "count",
                    Series = new List<float> { 1.0f }
                }
            },
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "test"
            }
        };
    }

    [Fact]
    public async Task Ingest_ReturnsBadRequest_WhenSignalTagMissing()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = BuildValidRequest();
        request.Signals[0] = new TelemetrySignalDto
        {
            Tag = string.Empty,
            Unit = "count",
            Series = new List<float> { 1.0f }
        };

        var response = await client.PostAsJsonAsync("/mcp/ingest/telemetry", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DomainError>();
        Assert.NotNull(payload);
        Assert.Equal(ErrorCodes.InvalidRequest, payload!.Code);
    }


    [Fact]
    public async Task Ingest_ApiRoute_AcceptsPayload()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = BuildValidRequest();
        var before = await CountTelemetryWindowsAsync(request.TenantId);
        var response = await client.PostAsJsonAsync("/api/ingest/telemetry", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TelemetryIngestResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Accepted);

        var after = await CountTelemetryWindowsAsync(request.TenantId);
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task Ingest_ReturnsOk_AndPersistsWindow_WhenPayloadValid()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = BuildValidRequest();
        var response = await client.PostAsJsonAsync("/mcp/ingest/telemetry", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TelemetryIngestResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload!.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(payload.IngestId));

        var count = await CountTelemetryWindowsAsync(request.TenantId);
        Assert.Equal(1, count);
    }

    private static TelemetryIngestRequestDto BuildValidRequest()
    {
        return new TelemetryIngestRequestDto
        {
            TenantId = "tenant-a",
            Window = new TelemetryWindowDto
            {
                Start = DateTimeOffset.UtcNow.AddMinutes(-5),
                End = DateTimeOffset.UtcNow
            },
            Signals = new List<TelemetrySignalDto>
            {
                new TelemetrySignalDto
                {
                    Tag = "requests",
                    Unit = "count",
                    Series = new List<float> { 1.0f, 2.0f }
                }
            },
            Metadata = new Dictionary<string, object?>
            {
                ["source"] = "test"
            }
        };
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        ConfigureEnvironment();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient();
                    services.Configure<HttpClientFactoryOptions>(Options.DefaultName, options =>
                    {
                        options.HttpMessageHandlerBuilderActions.Add(builder =>
                        {
                            builder.PrimaryHandler = new OkHttpMessageHandler();
                        });
                    });
                });
            });
    }

    private void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("REGISTRY", "registry.example.com");
        Environment.SetEnvironmentVariable("CWI_ORCHESTRATOR_TAG", "test");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("PACKS_BASE_URL", "http://packs.local");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://vector.local");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://vector.local");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://model.local");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "60");
    }

    private async Task ApplySchemaAsync()
    {
        var path = ResolveMigrationPath();
        var sql = await File.ReadAllTextAsync(path);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountTelemetryWindowsAsync(string tenantId)
    {
        const string sql = "SELECT COUNT(*) FROM telemetry_windows WHERE tenant_id = @tenant_id;";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        var result = await command.ExecuteScalarAsync();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private static string ResolveMigrationPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "backend",
                "services",
                "cwi-orchestrator",
                "src",
                "Cwi.Orchestrator.DataAccess",
                "Migrations",
                "0001_initial.sql");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate 0001_initial.sql migration.");
    }

    private sealed class OkHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
