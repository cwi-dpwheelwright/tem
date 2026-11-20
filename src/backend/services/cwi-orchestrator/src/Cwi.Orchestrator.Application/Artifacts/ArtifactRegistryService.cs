using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Artifacts;

public sealed class ArtifactRegistryService : DomainServiceBase
{
    private const string DefaultStatus = "registered";

    private readonly IArtifactRegistryRepository _repository;

    public ArtifactRegistryService(IArtifactRegistryRepository repository)
    {
        _repository = repository;
    }

    public Task<ServiceResponse<ArtifactRegistrationResponseDto>> RegisterAsync(
        ArtifactRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleRegisterAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<IReadOnlyList<ArtifactSummaryDto>>> ListAsync(
        string tenantId,
        string? artifactType,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, artifactType, cancellationToken));
    }

    public Task<ServiceResponse<ArtifactDetailDto>> GetAsync(
        string tenantId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(tenantId, artifactId, cancellationToken));
    }

    private async Task<ServiceResponse<ArtifactRegistrationResponseDto>> HandleRegisterAsync(
        ArtifactRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(request.ArtifactId))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "artifact_id is required."),
                statusCode: 400,
                message: "artifact_id required");
        }

        if (string.IsNullOrWhiteSpace(request.ArtifactType))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "artifact_type is required."),
                statusCode: 400,
                message: "artifact_type required");
        }

        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "version is required."),
                statusCode: 400,
                message: "version required");
        }

        if (string.IsNullOrWhiteSpace(request.Hash))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "hash is required."),
                statusCode: 400,
                message: "hash required");
        }

        var tenantId = request.TenantId.Trim();
        var artifactId = request.ArtifactId.Trim();
        var artifactType = request.ArtifactType.Trim();
        var version = request.Version.Trim();
        var hash = request.Hash.Trim();
        var now = DateTimeOffset.UtcNow;

        var existing = await _repository.GetAsync(tenantId, artifactId, cancellationToken).ConfigureAwait(false);
        var versions = existing is null
            ? new List<ArtifactVersion>()
            : DeserializeVersions(existing.VersionsJson);

        if (existing is not null && !string.Equals(existing.ArtifactType, artifactType, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "artifact_type mismatch."),
                statusCode: 409,
                message: "artifact_type mismatch");
        }

        var existingVersion = versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
        if (existingVersion is not null)
        {
            if (!string.Equals(existingVersion.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResponse<ArtifactRegistrationResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "artifact version hash mismatch."),
                    statusCode: 409,
                    message: "version conflict");
            }

            return ServiceResponse<ArtifactRegistrationResponseDto>.Success(new ArtifactRegistrationResponseDto
            {
                ArtifactId = artifactId,
                Version = existingVersion.Version,
                Status = existing?.Status ?? DefaultStatus,
                CreatedUtc = existingVersion.CreatedUtc
            });
        }

        versions.Add(new ArtifactVersion
        {
            Version = version,
            Hash = hash,
            ProvenanceRefs = request.ProvenanceRefs?.ToArray() ?? Array.Empty<string>(),
            Metadata = request.Metadata is null ? null : new Dictionary<string, string>(request.Metadata),
            CreatedUtc = now
        });

        var record = existing ?? new ArtifactRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ArtifactId = artifactId,
            CreatedUtc = now
        };

        record.ArtifactType = artifactType;
        record.Title = request.Title ?? record.Title;
        record.Status = record.Status is { Length: > 0 } ? record.Status : DefaultStatus;
        record.VersionsJson = JsonSerializer.Serialize(versions);
        record.UpdatedUtc = now;

        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<ArtifactRegistrationResponseDto>.Success(new ArtifactRegistrationResponseDto
        {
            ArtifactId = artifactId,
            Version = version,
            Status = record.Status,
            CreatedUtc = now
        });
    }

    private async Task<ServiceResponse<IReadOnlyList<ArtifactSummaryDto>>> HandleListAsync(
        string tenantId,
        string? artifactType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<IReadOnlyList<ArtifactSummaryDto>>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id required");
        }

        var records = await _repository.ListAsync(tenantId.Trim(), artifactType, cancellationToken).ConfigureAwait(false);
        var summaries = records
            .Select(record =>
            {
                var versions = DeserializeVersions(record.VersionsJson);
                var latest = versions.OrderByDescending(v => v.CreatedUtc).FirstOrDefault();
                return new ArtifactSummaryDto
                {
                    ArtifactId = record.ArtifactId,
                    ArtifactType = record.ArtifactType,
                    LatestVersion = latest?.Version ?? string.Empty,
                    Title = record.Title,
                    Status = record.Status
                };
            })
            .ToList();

        return ServiceResponse<IReadOnlyList<ArtifactSummaryDto>>.Success(summaries);
    }

    private async Task<ServiceResponse<ArtifactDetailDto>> HandleGetAsync(
        string tenantId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<ArtifactDetailDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(artifactId))
        {
            return ServiceResponse<ArtifactDetailDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "artifact_id is required."),
                statusCode: 400,
                message: "artifact_id required");
        }

        var record = await _repository.GetAsync(tenantId.Trim(), artifactId.Trim(), cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return ServiceResponse<ArtifactDetailDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "artifact not found."),
                statusCode: 404,
                message: "artifact not found");
        }

        var versions = DeserializeVersions(record.VersionsJson)
            .OrderByDescending(v => v.CreatedUtc)
            .Select(v => new ArtifactVersionDto
            {
                Version = v.Version,
                Hash = v.Hash,
                ProvenanceRefs = v.ProvenanceRefs,
                CreatedUtc = v.CreatedUtc
            })
            .ToList();

        return ServiceResponse<ArtifactDetailDto>.Success(new ArtifactDetailDto
        {
            ArtifactId = record.ArtifactId,
            ArtifactType = record.ArtifactType,
            Title = record.Title,
            Status = record.Status,
            Versions = versions
        });
    }

    private static List<ArtifactVersion> DeserializeVersions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ArtifactVersion>();
        }

        return JsonSerializer.Deserialize<List<ArtifactVersion>>(json) ?? new List<ArtifactVersion>();
    }

    private sealed class ArtifactVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("hash")]
        public string Hash { get; init; } = string.Empty;

        [JsonPropertyName("provenance_refs")]
        public IReadOnlyList<string> ProvenanceRefs { get; init; } = Array.Empty<string>();

        [JsonPropertyName("metadata")]
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }

        [JsonPropertyName("created_utc")]
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
