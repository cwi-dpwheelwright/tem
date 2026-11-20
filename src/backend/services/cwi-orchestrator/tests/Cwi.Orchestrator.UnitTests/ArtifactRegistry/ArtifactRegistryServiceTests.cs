using Cwi.Orchestrator.Application.Artifacts;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.ArtifactRegistry;

public sealed class ArtifactRegistryServiceTests
{
    [Fact]
    public async Task RegisterAsync_IsIdempotent_ForSameVersion()
    {
        var repository = new InMemoryArtifactRepository();
        var service = new ArtifactRegistryService(repository);

        var request = new ArtifactRegistrationRequestDto
        {
            TenantId = "tenant-1",
            ArtifactId = "artifact-1",
            ArtifactType = "report",
            Version = "v1",
            Hash = "hash-1",
            ProvenanceRefs = new[] { "trace-1" }
        };

        var first = await service.RegisterAsync(request, CancellationToken.None);
        var second = await service.RegisterAsync(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Data!.Version.Should().Be("v1");

        var list = await service.ListAsync("tenant-1", null, CancellationToken.None);
        list.IsSuccess.Should().BeTrue();
        list.Data!.Should().ContainSingle();
        list.Data![0].LatestVersion.Should().Be("v1");

        var detail = await service.GetAsync("tenant-1", "artifact-1", CancellationToken.None);
        detail.IsSuccess.Should().BeTrue();
        detail.Data!.Versions.Should().HaveCount(1);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsConflict_ForHashMismatch()
    {
        var repository = new InMemoryArtifactRepository();
        var service = new ArtifactRegistryService(repository);

        var request = new ArtifactRegistrationRequestDto
        {
            TenantId = "tenant-1",
            ArtifactId = "artifact-1",
            ArtifactType = "report",
            Version = "v1",
            Hash = "hash-1"
        };

        var first = await service.RegisterAsync(request, CancellationToken.None);
        var conflictRequest = new ArtifactRegistrationRequestDto
        {
            TenantId = request.TenantId,
            ArtifactId = request.ArtifactId,
            ArtifactType = request.ArtifactType,
            Version = request.Version,
            Hash = "hash-2"
        };

        var conflict = await service.RegisterAsync(conflictRequest, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        conflict.IsSuccess.Should().BeFalse();
        conflict.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ListAsync_handles_large_registry()
    {
        var repository = new InMemoryArtifactRepository();
        var service = new ArtifactRegistryService(repository);

        for (var i = 0; i < 250; i++)
        {
            var request = new ArtifactRegistrationRequestDto
            {
                TenantId = "tenant-1",
                ArtifactId = $"artifact-{i}",
                ArtifactType = i % 2 == 0 ? "report" : "document",
                Version = "v1",
                Hash = $"hash-{i}",
                Title = $"Artifact {i}"
            };

            var result = await service.RegisterAsync(request, CancellationToken.None);
            result.IsSuccess.Should().BeTrue();
        }

        var list = await service.ListAsync("tenant-1", null, CancellationToken.None);

        list.IsSuccess.Should().BeTrue();
        list.Data!.Should().HaveCount(250);
    }


    private sealed class InMemoryArtifactRepository : IArtifactRegistryRepository
    {
        private readonly Dictionary<string, ArtifactRecord> _records = new();

        public Task<ArtifactRecord?> GetAsync(string tenantId, string artifactId, CancellationToken cancellationToken)
        {
            var key = Key(tenantId, artifactId);
            _records.TryGetValue(key, out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<ArtifactRecord>> ListAsync(string tenantId, string? artifactType, CancellationToken cancellationToken)
        {
            var results = _records.Values
                .Where(record => record.TenantId == tenantId)
                .Where(record => string.IsNullOrWhiteSpace(artifactType) || record.ArtifactType == artifactType)
                .OrderBy(record => record.ArtifactId)
                .ToList();

            return Task.FromResult<IReadOnlyList<ArtifactRecord>>(results);
        }

        public Task UpsertAsync(ArtifactRecord record, CancellationToken cancellationToken)
        {
            _records[Key(record.TenantId, record.ArtifactId)] = record;
            return Task.CompletedTask;
        }

        private static string Key(string tenantId, string artifactId)
        {
            return $"{tenantId}:{artifactId}";
        }
    }
}
