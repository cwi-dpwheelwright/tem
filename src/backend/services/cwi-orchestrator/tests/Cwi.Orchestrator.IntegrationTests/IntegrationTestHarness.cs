using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class IntegrationTestHarness : IAsyncLifetime
{
    private const string PostgresUser = "cwi";
    private const string PostgresPassword = "cwi";
    private const string PostgresDb = "cwi";

    private readonly TestcontainersContainer _postgres;
    private readonly TestcontainersContainer _redis;

    public IntegrationTestHarness()
    {
        _postgres = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("postgres:16-alpine")
            .WithEnvironment("POSTGRES_USER", PostgresUser)
            .WithEnvironment("POSTGRES_PASSWORD", PostgresPassword)
            .WithEnvironment("POSTGRES_DB", PostgresDb)
            .WithPortBinding(0, 5432)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        _redis = new TestcontainersBuilder<TestcontainersContainer>()
            .WithImage("redis:7-alpine")
            .WithPortBinding(0, 6379)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(6379))
            .Build();
    }

    public string PostgresConnectionString
    {
        get
        {
            var port = _postgres.GetMappedPublicPort(5432);
            return $"Host=localhost;Port={port};Database={PostgresDb};Username={PostgresUser};Password={PostgresPassword}";
        }
    }

    public string RedisConnectionString
    {
        get
        {
            var port = _redis.GetMappedPublicPort(6379);
            return $"localhost:{port}";
        }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
