using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Pipeline;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using System.Text.Json;

namespace Cwi.Coordination.Application.Services;

public sealed class MembershipService : IMembershipService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMembershipRepository _repository;
    private readonly MembershipPipeline _pipeline;

    public MembershipService(IMembershipRepository repository, MembershipPipeline pipeline)
    {
        _repository = repository;
        _pipeline = pipeline;
    }

    public async Task<ServiceResponse<EnrollmentResponse>> EnrollAsync(
        EnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _pipeline.ExecuteAsync(
                request,
                Guid.NewGuid().ToString("n"),
                async () =>
                {
                    var nowUtc = DateTimeOffset.UtcNow;
                    var existing = await _repository.GetAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
                    if (existing is not null && existing.Status == MembershipStatus.Active)
                    {
                        if (!string.Equals(
                                existing.CertificateFingerprint,
                                request.CertificateFingerprint,
                                StringComparison.Ordinal))
                        {
                            throw new DomainException(
                                new DomainError(ErrorCodes.InvalidRequest, "Node already enrolled with a different certificate fingerprint."),
                                409);
                        }

                        existing.TenantId = request.TenantId;
                        existing.OwnerUserId = request.OwnerUserId;
                        existing.DeviceClass = request.DeviceClass;
                        existing.TailscaleIp = request.TailscaleIp;
                        existing.WatcherNodeId = request.WatcherNodeId;
                        existing.CapabilitiesJson = SerializeDictionary(request.Capabilities);
                        existing.PackVersionsJson = SerializeDictionary(request.PackVersions);
                        existing.PolicyProfileVersion = request.PolicyProfileVersion;
                        existing.HealthJson = SerializeDictionary(request.Health);
                        existing.LastSeenUtc = nowUtc;
                        existing.RevokedUtc = null;
                        await _repository.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);

                        return new EnrollmentResponse
                        {
                            NodeId = existing.NodeId,
                            Status = existing.Status.ToString().ToLowerInvariant()
                        };
                    }

                    var member = existing ?? new Member
                    {
                        NodeId = request.NodeId,
                    };
                    member.TenantId = request.TenantId;
                    member.OwnerUserId = request.OwnerUserId;
                    member.DeviceClass = request.DeviceClass;
                    member.TailscaleIp = request.TailscaleIp;
                    member.WatcherNodeId = request.WatcherNodeId;
                    member.CapabilitiesJson = SerializeDictionary(request.Capabilities);
                    member.PackVersionsJson = SerializeDictionary(request.PackVersions);
                    member.PolicyProfileVersion = request.PolicyProfileVersion;
                    member.HealthJson = SerializeDictionary(request.Health);
                    member.CertificateFingerprint = request.CertificateFingerprint;
                    member.Status = MembershipStatus.Active;
                    member.LastSeenUtc = nowUtc;
                    member.RevokedUtc = null;

                    if (existing is null)
                    {
                        await _repository.AddAsync(member, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _repository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
                    }

                    return new EnrollmentResponse
                    {
                        NodeId = member.NodeId,
                        Status = member.Status.ToString().ToLowerInvariant()
                    };
                },
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<EnrollmentResponse>.Success(response);
        }
        catch (DomainException exc)
        {
            return ServiceResponse<EnrollmentResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<MemberResponse>> RotateAsync(RotationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NodeId) || string.IsNullOrWhiteSpace(request.NewCertificateFingerprint))
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "node_id and new_cert_fingerprint required."),
                    400);
            }

            var member = await _repository.GetAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
            if (member is null)
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "Node not found."),
                    404);
            }

            member.CertificateFingerprint = request.NewCertificateFingerprint;
            await _repository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
            return ServiceResponse<MemberResponse>.Success(ToResponse(member));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<MemberResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<MemberResponse>> RevokeAsync(RevokeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NodeId))
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "node_id required."), 400);
            }

            var member = await _repository.GetAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
            if (member is null)
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "Node not found."),
                    404);
            }

            member.Status = MembershipStatus.Revoked;
            member.RevokedUtc = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
            return ServiceResponse<MemberResponse>.Success(ToResponse(member));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<MemberResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<IReadOnlyList<MemberResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var members = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var response = members.Select(ToResponse).ToList();
        return ServiceResponse<IReadOnlyList<MemberResponse>>.Success(response);
    }

    private static MemberResponse ToResponse(Member member)
    {
        return new MemberResponse
        {
            NodeId = member.NodeId,
            TenantId = member.TenantId,
            OwnerUserId = member.OwnerUserId,
            DeviceClass = member.DeviceClass,
            TailscaleIp = member.TailscaleIp,
            WatcherNodeId = member.WatcherNodeId,
            Capabilities = DeserializeDictionary(member.CapabilitiesJson),
            PackVersions = DeserializeDictionary(member.PackVersionsJson),
            PolicyProfileVersion = member.PolicyProfileVersion,
            Health = DeserializeDictionary(member.HealthJson),
            CertificateFingerprint = member.CertificateFingerprint,
            Status = member.Status.ToString().ToLowerInvariant(),
            JoinedUtc = member.JoinedUtc,
            LastSeenUtc = member.LastSeenUtc,
            RevokedUtc = member.RevokedUtc
        };
    }

    private static string SerializeDictionary(Dictionary<string, object?> value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), JsonOptions);
    }

    private static Dictionary<string, object?> DeserializeDictionary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, object?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(value, JsonOptions)
            ?? new Dictionary<string, object?>();
    }
}
