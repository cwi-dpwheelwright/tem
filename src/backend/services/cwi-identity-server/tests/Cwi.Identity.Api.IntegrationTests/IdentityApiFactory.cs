using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Cwi.Identity.Api.Data;
using Xunit;

namespace Cwi.Identity.Api.IntegrationTests;

public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IContainer _postgres;
    private string _connectionString = "";
    private readonly string _keyEncryptionKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public IdentityApiFactory()
    {
        Environment.SetEnvironmentVariable("IDENTITY_CORS_ORIGINS", "https://identity.test");
        Environment.SetEnvironmentVariable("IDENTITY_KEY_ENCRYPTION_KEY", _keyEncryptionKey);
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_TENANT_ID", "admin");
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_USERNAME", "superadmin");
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_EMAIL", "superadmin@local");
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_PASSWORD", "ValidPassw0rd!");
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_NAME", "Super Admin");
        Environment.SetEnvironmentVariable("IDENTITY_SUPERADMIN_LOGIN_TENANT_ID", "CWI-ops");
        Environment.SetEnvironmentVariable("IDENTITY_LOGIN_TENANT_PRIORITY", "CWI-ops,admin");
        Environment.SetEnvironmentVariable("IDENTITY_PLATFORM_ADMIN_ROLE", "platform_admin");
        Environment.SetEnvironmentVariable("IDENTITY_DEFAULT_TENANT", "tenant-1");
        Environment.SetEnvironmentVariable("IDENTITY_DEV_SKIP_VERIFICATION_DELIVERY", "true");
        Environment.SetEnvironmentVariable("IDENTITY_DEV_VERIFICATION_CODE", "123456");

        _postgres = new ContainerBuilder()
            .WithImage("postgres:16")
            .WithEnvironment("POSTGRES_DB", "cwi_identity")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<IdentityDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(_connectionString));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var host = _postgres.Hostname;
        var port = _postgres.GetMappedPublicPort(5432);
        _connectionString = $"Host={host};Port={port};Database=cwi_identity;Username=postgres;Password=postgres";
        Environment.SetEnvironmentVariable("IDENTITY_DB_CONNECTION", _connectionString);

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using var db = new IdentityDbContext(options);
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
