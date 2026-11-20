using System.Net.Http.Json;
using Cwi.Coordination.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Cwi.Coordination.IntegrationTests;

public sealed class PlacementMetricsIntegrationTests
{
    [DockerFact]
    public async Task PlacementMetrics_Persist_Using_Postgres()
    {
        await using var container = new ContainerBuilder("postgres:16-alpine")
            .WithEnvironment("POSTGRES_DB", "coordination")
            .WithEnvironment("POSTGRES_USER", "cwi")
            .WithEnvironment("POSTGRES_PASSWORD", "cwi")
            .WithPortBinding(5432, true)
            .Build();

        await container.StartAsync();
        await Task.Delay(500);
        var port = container.GetMappedPublicPort(5432);
        var connectionString = $"Host=localhost;Port={port};Database=coordination;Username=cwi;Password=cwi;Ssl Mode=Disable;Trust Server Certificate=true";

        await ApplyMigrationsAsync(connectionString);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Transport:RequireClientCertificate"] = "false",
                        ["Coordination:RequireClientCertificate"] = "false",
                        ["Coordination:Admin:RequireToken"] = "false",
                        ["Coordination:Storage:ConnectionString"] = connectionString,
                        ["Coordination:Storage:UseInMemory"] = "false"
                    });
                });
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Coordination-Protocol", "1.0");

        var request = new PlacementMetricsRequest
        {
            MetricsId = "mpm-db-1",
            NodeId = "node-db",
            ObservedUtc = DateTimeOffset.UtcNow,
            Radio = new PlacementRadioMetrics
            {
                LatencyMs = 88,
                PacketLoss = 0.02,
                RssiDbm = -64,
                SnrDb = 17,
                ThroughputMbps = 7.5,
                BandwidthMbps = 12
            },
            Environment = new PlacementEnvironmentMetrics
            {
                TemperatureC = 11,
                HumidityPct = 60
            },
            Uncertainty = new PlacementUncertaintyMetrics
            {
                Confidence = 0.79
            },
            Metadata = new Dictionary<string, object?>
            {
                ["lat"] = 34.05,
                ["lon"] = -118.24
            }
        };

        var ingest = await client.PostAsJsonAsync("/api/placement/metrics", request);
        ingest.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<IReadOnlyList<PlacementMetricsResponse>>(
            "/api/placement/metrics?node_id=node-db&limit=5");

        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("mpm-db-1", list![0].MetricsId);
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        await using var connection = await OpenWithRetryAsync(connectionString, 10, TimeSpan.FromMilliseconds(500));

        var migrationsPath = FindMigrationsDirectory();
        var scripts = new[]
        {
            "0001_coordination_tables.sql",
            "0002_placement_metrics.sql"
        };

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(migrationsPath, script));
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string FindMigrationsDirectory()
    {
        var parts = new[]
        {
            "src", "backend", "services", "cwi-coordination-layer", "src", "Cwi.Coordination.DataAccess", "Migrations"
        };

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate coordination-layer migrations directory.");
    }

    private static async Task<NpgsqlConnection> OpenWithRetryAsync(
        string connectionString,
        int attempts,
        TimeSpan delay)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return connection;
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(delay);
            }
        }

        var fallback = new NpgsqlConnection(connectionString);
        await fallback.OpenAsync();
        return fallback;
    }
}
