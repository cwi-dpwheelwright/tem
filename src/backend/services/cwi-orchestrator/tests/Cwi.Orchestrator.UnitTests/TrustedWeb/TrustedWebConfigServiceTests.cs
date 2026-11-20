using System.Collections.Concurrent;
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.TrustedWeb;

public sealed class TrustedWebConfigServiceTests
{
    [Fact]
    public async Task SaveAsync_merges_trusted_web_config_into_global_config()
    {
        var repository = new InMemoryTenantConfigRepository();
        await repository.UpsertAsync(new TenantConfig
        {
            Id = Guid.NewGuid(),
            TenantId = TrustedWebConfigService.GlobalConfigTenantId,
            ConfigJson = """{"policy_overrides":{"mode":"observe"}}""",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var service = new TrustedWebConfigService(repository);

        var result = await service.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        MaxResults = 4,
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "trusted-docs",
                                DisplayName = "Trusted Docs",
                                AllowedDomains = new[] { "https://Docs.Example.com/" },
                                SeedUrls = new[] { "https://docs.example.com/news" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var record = await repository.GetAsync(TrustedWebConfigService.GlobalConfigTenantId, CancellationToken.None);
        record.Should().NotBeNull();
        using var document = JsonDocument.Parse(record!.ConfigJson);
        document.RootElement.GetProperty("policy_overrides").GetProperty("mode").GetString().Should().Be("observe");
        var trustedWeb = document.RootElement.GetProperty("trusted_web_context");
        trustedWeb.GetProperty("enabled").GetBoolean().Should().BeTrue();
        trustedWeb.GetProperty("default_profile").GetString().Should().Be("general");
        trustedWeb.GetProperty("updated_by").GetString().Should().Be("admin-user");
        var source = trustedWeb.GetProperty("profiles").GetProperty("general").GetProperty("sources")[0];
        source.GetProperty("allowed_domains")[0].GetString().Should().Be("docs.example.com");
    }

    [Fact]
    public async Task LoadRuntimeConfigAsync_returns_global_profiles_document_from_database()
    {
        var repository = new InMemoryTenantConfigRepository();
        var service = new TrustedWebConfigService(repository);
        await service.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        MaxResults = 1,
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "trusted-news",
                                DisplayName = "Trusted News",
                                AllowedDomains = new[] { "trusted.example" },
                                SeedUrls = new[] { "https://trusted.example/news" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);

        var runtime = await service.LoadRuntimeConfigAsync("tenant-1", CancellationToken.None);

        runtime.DatabaseConfigured.Should().BeTrue();
        runtime.SkippedReason.Should().BeNull();
        runtime.Profiles.Should().NotBeNull();
        runtime.Profiles!.DefaultProfile.Should().Be("general");
        runtime.Profiles.Profiles["general"].Sources.Should().ContainSingle(source =>
            source.SourceId == "trusted-news"
            && source.SeedUrls.Contains("https://trusted.example/news"));
    }

    [Fact]
    public async Task LoadRuntimeConfigAsync_ignores_request_tenant_when_global_config_exists()
    {
        var repository = new InMemoryTenantConfigRepository();
        var service = new TrustedWebConfigService(repository);
        await service.SaveAsync(
            "tenant-a",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "global-source",
                                AllowedDomains = new[] { "global.example" },
                                SeedUrls = new[] { "https://global.example/reference" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);

        var tenantB = await service.LoadRuntimeConfigAsync("tenant-b", CancellationToken.None);

        tenantB.DatabaseConfigured.Should().BeTrue();
        tenantB.Profiles!.Profiles["general"].Sources.Should().ContainSingle(source =>
            source.SourceId == "global-source"
            && source.SeedUrls.Contains("https://global.example/reference"));
        (await repository.GetAsync("tenant-a", CancellationToken.None)).Should().BeNull();
        (await repository.GetAsync("tenant-b", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task LoadRuntimeConfigAsync_marks_database_config_disabled_without_falling_back()
    {
        var repository = new InMemoryTenantConfigRepository();
        var service = new TrustedWebConfigService(repository);
        await service.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = false,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new() { Mode = "trusted_sites_only" }
                }
            },
            "admin-user",
            CancellationToken.None);

        var runtime = await service.LoadRuntimeConfigAsync("tenant-1", CancellationToken.None);

        runtime.DatabaseConfigured.Should().BeTrue();
        runtime.Profiles.Should().BeNull();
        runtime.SkippedReason.Should().Be("trusted_web_profile_disabled");
    }

    private sealed class InMemoryTenantConfigRepository : ITenantConfigRepository
    {
        private readonly ConcurrentDictionary<string, TenantConfig> _records = new(StringComparer.OrdinalIgnoreCase);

        public Task<TenantConfig?> GetAsync(string tenantId, CancellationToken cancellationToken)
        {
            _records.TryGetValue(tenantId, out var config);
            return Task.FromResult(config);
        }

        public Task UpsertAsync(TenantConfig config, CancellationToken cancellationToken)
        {
            _records.AddOrUpdate(config.TenantId, config, (_, existing) =>
            {
                existing.ConfigJson = config.ConfigJson;
                existing.UpdatedUtc = config.UpdatedUtc;
                return existing;
            });
            return Task.CompletedTask;
        }
    }
}
