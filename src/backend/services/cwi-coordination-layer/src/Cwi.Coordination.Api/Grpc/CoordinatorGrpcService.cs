using Cwi.Common;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Services;
using Grpc.Core;
using AppModels = Cwi.Coordination.Application.Models;
using GrpcModels = Cwi.Coordination.Grpc;
using System.Text.Json;

namespace Cwi.Coordination.Api.Grpc;

public sealed class CoordinatorGrpcService : GrpcModels.Coordinator.CoordinatorBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMembershipService _membershipService;
    private readonly RoundService _roundService;
    private readonly ApplicationOptions _options;

    public CoordinatorGrpcService(
        IMembershipService membershipService,
        RoundService roundService,
        ApplicationOptions options)
    {
        _membershipService = membershipService;
        _roundService = roundService;
        _options = options;
    }

    public override async Task<GrpcModels.EnrollmentResponse> Enroll(
        GrpcModels.EnrollmentRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _membershipService.EnrollAsync(new AppModels.EnrollmentRequest
        {
            NodeId = request.NodeId,
            TenantId = request.TenantId,
            OwnerUserId = request.OwnerUserId,
            DeviceClass = request.DeviceClass,
            TailscaleIp = request.TailscaleIp,
            WatcherNodeId = request.WatcherNodeId,
            Capabilities = DeserializeDictionary(request.CapabilitiesJson, "capabilities_json"),
            PackVersions = DeserializeDictionary(request.PackVersionsJson, "pack_versions_json"),
            PolicyProfileVersion = string.IsNullOrWhiteSpace(request.PolicyProfileVersion) ? "1.0" : request.PolicyProfileVersion,
            Health = DeserializeDictionary(request.HealthJson, "health_json"),
            CertificateFingerprint = request.CertFingerprint,
            ProtocolVersion = string.IsNullOrWhiteSpace(request.ProtocolVersion) ? "1.0" : request.ProtocolVersion
        }, context.CancellationToken).ConfigureAwait(false);

        var data = RequireSuccess(response);
        return new GrpcModels.EnrollmentResponse
        {
            NodeId = data.NodeId,
            Status = data.Status
        };
    }

    public override async Task<GrpcModels.MemberResponse> Rotate(
        GrpcModels.RotationRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _membershipService.RotateAsync(new AppModels.RotationRequest
        {
            NodeId = request.NodeId,
            NewCertificateFingerprint = request.NewCertFingerprint
        }, context.CancellationToken).ConfigureAwait(false);

        return MapMember(RequireSuccess(response));
    }

    public override async Task<GrpcModels.MemberResponse> Revoke(
        GrpcModels.RevokeRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _membershipService.RevokeAsync(new AppModels.RevokeRequest
        {
            NodeId = request.NodeId,
            Reason = request.Reason
        }, context.CancellationToken).ConfigureAwait(false);

        return MapMember(RequireSuccess(response));
    }

    public override async Task<GrpcModels.ListMembersResponse> ListMembers(
        GrpcModels.ListMembersRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _membershipService.ListAsync(context.CancellationToken).ConfigureAwait(false);
        var data = RequireSuccess(response);
        var result = new GrpcModels.ListMembersResponse();
        result.Members.AddRange(data.Select(MapMember));
        return result;
    }

    public override async Task<GrpcModels.RoundResponse> CreateRound(
        GrpcModels.RoundCreateRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _roundService.CreateAsync(new AppModels.RoundCreateRequest
        {
            Name = request.Name
        }, context.CancellationToken).ConfigureAwait(false);

        return MapRound(RequireSuccess(response));
    }

    public override async Task<GrpcModels.ListRoundsResponse> ListRounds(
        GrpcModels.ListRoundsRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        var response = await _roundService.ListAsync(context.CancellationToken).ConfigureAwait(false);
        var data = RequireSuccess(response);
        var result = new GrpcModels.ListRoundsResponse();
        result.Rounds.AddRange(data.Select(MapRound));
        return result;
    }

    public override async Task<GrpcModels.RoundResponse> Heartbeat(
        GrpcModels.HeartbeatRequest request,
        ServerCallContext context)
    {
        EnsureProtocol(context);
        if (!Guid.TryParse(request.RoundId, out var roundId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "round_id must be a GUID."));
        }

        var response = await _roundService.HeartbeatAsync(
            roundId,
            new AppModels.HeartbeatRequest
            {
                NodeId = request.NodeId,
                Status = request.Status
            },
            context.CancellationToken).ConfigureAwait(false);

        return MapRound(RequireSuccess(response));
    }

    private static GrpcModels.MemberResponse MapMember(AppModels.MemberResponse member)
    {
        return new GrpcModels.MemberResponse
        {
            NodeId = member.NodeId,
            CertFingerprint = member.CertificateFingerprint,
            Status = member.Status,
            JoinedUtc = member.JoinedUtc.ToString("O"),
            RevokedUtc = member.RevokedUtc?.ToString("O") ?? string.Empty,
            TenantId = member.TenantId,
            OwnerUserId = member.OwnerUserId,
            DeviceClass = member.DeviceClass ?? string.Empty,
            TailscaleIp = member.TailscaleIp ?? string.Empty,
            WatcherNodeId = member.WatcherNodeId ?? string.Empty,
            CapabilitiesJson = SerializeDictionary(member.Capabilities),
            PackVersionsJson = SerializeDictionary(member.PackVersions),
            PolicyProfileVersion = member.PolicyProfileVersion,
            HealthJson = SerializeDictionary(member.Health),
            LastSeenUtc = member.LastSeenUtc?.ToString("O") ?? string.Empty
        };
    }

    private static GrpcModels.RoundResponse MapRound(AppModels.RoundResponse round)
    {
        return new GrpcModels.RoundResponse
        {
            RoundId = round.RoundId.ToString(),
            Name = round.Name,
            Status = round.Status
        };
    }

    private static T RequireSuccess<T>(ServiceResponse<T> response)
    {
        if (response.IsSuccess && response.Data is not null)
        {
            return response.Data;
        }

        throw ToRpcException(response);
    }

    private static RpcException ToRpcException<T>(ServiceResponse<T> response)
    {
        var statusCode = response.StatusCode;
        var grpcStatus = statusCode switch
        {
            400 => StatusCode.InvalidArgument,
            401 => StatusCode.Unauthenticated,
            403 => StatusCode.PermissionDenied,
            404 => StatusCode.NotFound,
            409 => StatusCode.FailedPrecondition,
            422 => StatusCode.FailedPrecondition,
            503 => StatusCode.Unavailable,
            _ => StatusCode.Unknown
        };

        var message = response.Error?.Message ?? response.Message ?? "request failed";
        return new RpcException(new Status(grpcStatus, message));
    }

    private void EnsureProtocol(ServerCallContext context)
    {
        var header = context.RequestHeaders.FirstOrDefault(h => h.Key == "x-coordination-protocol")?.Value;
        if (string.IsNullOrWhiteSpace(header) || !string.Equals(header, _options.ProtocolVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Protocol version mismatch."));
        }
    }

    private static Dictionary<string, object?> DeserializeDictionary(string json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"{fieldName} must be valid JSON."));
        }
    }

    private static string SerializeDictionary(Dictionary<string, object?> value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), JsonOptions);
    }
}
