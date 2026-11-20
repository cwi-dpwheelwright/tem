#nullable enable
using System.Net;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class CurrentUserHomeNodeLinkService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHomeNodeRepository _homeNodeRepository;
    private readonly IHomeNodeAssignmentRepository _homeNodeAssignmentRepository;
    private readonly HomeNodeAssignmentService _assignmentService;
    private readonly HomeNodeDispatchClient _dispatchClient;

    public CurrentUserHomeNodeLinkService(
        IHomeNodeRepository homeNodeRepository,
        IHomeNodeAssignmentRepository homeNodeAssignmentRepository,
        HomeNodeAssignmentService assignmentService,
        HomeNodeDispatchClient dispatchClient)
    {
        _homeNodeRepository = homeNodeRepository;
        _homeNodeAssignmentRepository = homeNodeAssignmentRepository;
        _assignmentService = assignmentService;
        _dispatchClient = dispatchClient;
    }

    public Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> GetAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(tenantId, userId, cancellationToken));
    }

    public Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> LinkAsync(
        string tenantId,
        string userId,
        CurrentUserHomeNodeLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleLinkAsync(tenantId, userId, request, cancellationToken));
    }

    public Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> UnlinkAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleUnlinkAsync(tenantId, userId, cancellationToken));
    }

    private async Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> HandleGetAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail("tenant_id is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail("user_id is required.", 400);
        }

        var normalizedTenantId = tenantId.Trim();
        var normalizedUserId = userId.Trim();
        var assignment = await _homeNodeAssignmentRepository
            .GetAsync(normalizedTenantId, normalizedUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsActiveAssignment(assignment))
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Success(
                BuildResponse(
                    normalizedTenantId,
                    normalizedUserId,
                    null,
                    null,
                    null,
                    null));
        }

        var node = assignment is null
            ? null
            : await _homeNodeRepository.GetAsync(normalizedTenantId, assignment.PrimaryHomeNodeId, cancellationToken)
                .ConfigureAwait(false);

        return ServiceResponse<CurrentUserHomeNodeLinkDto>.Success(
            BuildResponse(
                normalizedTenantId,
                normalizedUserId,
                assignment is null ? null : MapAssignment(assignment),
                node,
                null,
                null));
    }

    private async Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> HandleLinkAsync(
        string tenantId,
        string userId,
        CurrentUserHomeNodeLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail("tenant_id is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail("user_id is required.", 400);
        }

        var normalizedTenantId = tenantId.Trim();
        var normalizedUserId = userId.Trim();
        var parseResult = ParseReference(request);
        if (!string.IsNullOrWhiteSpace(parseResult.ErrorMessage))
        {
            return Fail(parseResult.ErrorMessage!, 400);
        }

        var reference = parseResult.Reference;
        if (reference is null)
        {
            return Fail("A node identifier or QR payload is required.", 400);
        }

        if (!string.IsNullOrWhiteSpace(reference.TenantId) &&
            !string.Equals(reference.TenantId, normalizedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Scanned node tenant does not match the current session."),
                statusCode: 403,
                message: "tenant mismatch");
        }

        var candidateResult = await ResolveNodeAsync(
                normalizedTenantId,
                normalizedUserId,
                reference,
                cancellationToken)
            .ConfigureAwait(false);
        if (!candidateResult.IsSuccess)
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
                candidateResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, candidateResult.Message),
                candidateResult.StatusCode,
                candidateResult.Message);
        }

        var candidate = candidateResult.Data;
        if (candidate is null)
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Scanned node identifier does not match a registered home node."),
                statusCode: 404,
                message: "home node not found");
        }

        if (IsUnclaimedOwner(candidate.OwnerUserId))
        {
            candidate = await ClaimNodeAsync(candidate, normalizedUserId, cancellationToken).ConfigureAwait(false);
        }

        var upsert = await _assignmentService.UpsertAsync(
            new HomeNodeAssignmentUpsertRequestDto
            {
                TenantId = normalizedTenantId,
                UserId = normalizedUserId,
                PrimaryHomeNodeId = candidate.NodeId,
                WatcherNodeId = reference.WatcherNodeId,
                PolicyScopeId = reference.PolicyScopeId,
                LocalOnly = reference.LocalOnly ?? request.LocalOnly,
                RemoteFallbackAllowed = reference.RemoteFallbackAllowed ?? request.RemoteFallbackAllowed,
                Status = "active"
            },
            cancellationToken).ConfigureAwait(false);

        if (!upsert.IsSuccess || upsert.Data is null)
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
                upsert.Error ?? new DomainError(ErrorCodes.InvalidRequest, upsert.Message ?? "Unable to link home node."),
                upsert.StatusCode,
                upsert.Message ?? "Unable to link home node.");
        }

        return ServiceResponse<CurrentUserHomeNodeLinkDto>.Success(
            BuildResponse(
                normalizedTenantId,
                normalizedUserId,
                upsert.Data,
                candidate,
                reference.MatchedIdentifier,
                reference.MatchedVia));
    }

    private async Task<ServiceResponse<CurrentUserHomeNodeLinkDto>> HandleUnlinkAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail("tenant_id is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail("user_id is required.", 400);
        }

        var normalizedTenantId = tenantId.Trim();
        var normalizedUserId = userId.Trim();
        var assignment = await _homeNodeAssignmentRepository
            .GetAsync(normalizedTenantId, normalizedUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!IsActiveAssignment(assignment))
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Success(
                BuildResponse(
                    normalizedTenantId,
                    normalizedUserId,
                    null,
                    null,
                    null,
                    null));
        }

        var revoked = await _assignmentService.UpsertAsync(
            new HomeNodeAssignmentUpsertRequestDto
            {
                TenantId = normalizedTenantId,
                UserId = normalizedUserId,
                PrimaryHomeNodeId = assignment!.PrimaryHomeNodeId,
                WatcherNodeId = assignment.WatcherNodeId,
                PolicyScopeId = assignment.PolicyScopeId,
                LocalOnly = assignment.LocalOnly,
                RemoteFallbackAllowed = assignment.RemoteFallbackAllowed,
                Status = "revoked",
                AssignedUtc = assignment.AssignedUtc
            },
            cancellationToken).ConfigureAwait(false);
        if (!revoked.IsSuccess)
        {
            return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
                revoked.Error ?? new DomainError(ErrorCodes.InvalidRequest, revoked.Message ?? "Unable to unlink home node."),
                revoked.StatusCode,
                revoked.Message ?? "Unable to unlink home node.");
        }

        return ServiceResponse<CurrentUserHomeNodeLinkDto>.Success(
            BuildResponse(
                normalizedTenantId,
                normalizedUserId,
                null,
                null,
                null,
                null));
    }

    private async Task<ServiceResponse<HomeNodeRecord?>> ResolveNodeAsync(
        string tenantId,
        string userId,
        ParsedNodeReference reference,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(reference.NodeId))
        {
            var node = await _homeNodeRepository.GetAsync(tenantId, reference.NodeId, cancellationToken).ConfigureAwait(false);
            if (node is not null)
            {
                return await RefreshRegisteredNodeAsync(node, cancellationToken).ConfigureAwait(false);
            }

            var claimedInAnotherTenant = await _homeNodeRepository.FindByNodeIdAsync(reference.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (claimedInAnotherTenant is not null &&
                !string.Equals(claimedInAnotherTenant.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResponse<HomeNodeRecord?>.Fail(
                    new DomainError(ErrorCodes.Forbidden, "The scanned node is already linked in another tenant."),
                    statusCode: 403,
                    message: "tenant ownership mismatch");
            }

            return await ResolveEnrolledNodeAsync(tenantId, userId, reference, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(reference.TailscaleIp))
        {
            return ServiceResponse<HomeNodeRecord?>.Success(null);
        }

        var nodes = await _homeNodeRepository.ListAsync(tenantId, null, null, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<HomeNodeRecord?>.Success(nodes.FirstOrDefault(node =>
            string.Equals(node.TailscaleIp, reference.TailscaleIp, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<ServiceResponse<HomeNodeRecord?>> RefreshRegisteredNodeAsync(
        HomeNodeRecord node,
        CancellationToken cancellationToken)
    {
        if (!_dispatchClient.CanQueryMembership)
        {
            return ServiceResponse<HomeNodeRecord?>.Success(node);
        }

        var membershipResult = await _dispatchClient.GetMembershipAsync(node.NodeId, cancellationToken)
            .ConfigureAwait(false);
        if (!membershipResult.IsSuccess)
        {
            return membershipResult.StatusCode >= 500
                ? ServiceResponse<HomeNodeRecord?>.Success(node)
                : ServiceResponse<HomeNodeRecord?>.Fail(
                    membershipResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, membershipResult.Message),
                    membershipResult.StatusCode,
                    membershipResult.Message);
        }

        var membership = membershipResult.Data;
        if (membership is null)
        {
            return ServiceResponse<HomeNodeRecord?>.Success(node);
        }

        if (!string.IsNullOrWhiteSpace(membership.TenantId) &&
            !string.Equals(membership.TenantId, node.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<HomeNodeRecord?>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Scanned node tenant does not match the current session."),
                statusCode: 403,
                message: "tenant mismatch");
        }

        SyncNodeFromMembership(node, membership, preserveExistingOwnerWhenMembershipOwnerMissing: true);
        await _homeNodeRepository.UpsertAsync(node, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<HomeNodeRecord?>.Success(node);
    }

    private async Task<ServiceResponse<HomeNodeRecord?>> ResolveEnrolledNodeAsync(
        string tenantId,
        string userId,
        ParsedNodeReference reference,
        CancellationToken cancellationToken)
    {
        if (!_dispatchClient.CanQueryMembership || string.IsNullOrWhiteSpace(reference.NodeId))
        {
            return ServiceResponse<HomeNodeRecord?>.Success(null);
        }

        var membershipResult = await _dispatchClient.GetMembershipAsync(reference.NodeId, cancellationToken)
            .ConfigureAwait(false);
        if (!membershipResult.IsSuccess || membershipResult.Data is null)
        {
            return ServiceResponse<HomeNodeRecord?>.Success(null);
        }

        var membership = membershipResult.Data;
        if (!string.IsNullOrWhiteSpace(membership.TenantId) &&
            !string.Equals(membership.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<HomeNodeRecord?>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Scanned node tenant does not match the current session."),
                statusCode: 403,
                message: "tenant mismatch");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new HomeNodeRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NodeId = membership.NodeId,
            OwnerUserId = NormalizeNullable(membership.OwnerUserId) ?? string.Empty,
            DeviceClass = NormalizeNullable(membership.DeviceClass),
            TailscaleIp = NormalizeNullable(membership.TailscaleIp) ?? reference.TailscaleIp,
            WatcherNodeId = NormalizeNullable(membership.WatcherNodeId) ?? reference.WatcherNodeId,
            CapabilitiesJson = SerializeMap(membership.Capabilities),
            PackVersionsJson = SerializeMap(membership.PackVersions),
            PolicyProfileVersion = NormalizeNullable(membership.PolicyProfileVersion) ?? "1.0",
            Status = NormalizeNullable(membership.Status)?.ToLowerInvariant() ?? "active",
            LastHeartbeatUtc = membership.LastSeenUtc,
            AssignedUtc = now,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        if (string.IsNullOrWhiteSpace(record.OwnerUserId))
        {
            record.OwnerUserId = userId;
        }

        await _homeNodeRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<HomeNodeRecord?>.Success(record);
    }

    private static void SyncNodeFromMembership(
        HomeNodeRecord node,
        HomeNodeMembershipSnapshot membership,
        bool preserveExistingOwnerWhenMembershipOwnerMissing)
    {
        var membershipOwner = NormalizeNullable(membership.OwnerUserId);
        if (!string.IsNullOrWhiteSpace(membershipOwner) || !preserveExistingOwnerWhenMembershipOwnerMissing)
        {
            node.OwnerUserId = membershipOwner ?? string.Empty;
        }

        node.DeviceClass = NormalizeNullable(membership.DeviceClass) ?? node.DeviceClass;
        node.TailscaleIp = NormalizeNullable(membership.TailscaleIp) ?? node.TailscaleIp;
        node.WatcherNodeId = NormalizeNullable(membership.WatcherNodeId) ?? node.WatcherNodeId;
        node.CapabilitiesJson = SerializeMap(membership.Capabilities);
        node.PackVersionsJson = SerializeMap(membership.PackVersions);
        node.PolicyProfileVersion = NormalizeNullable(membership.PolicyProfileVersion) ?? node.PolicyProfileVersion;
        node.Status = NormalizeNullable(membership.Status)?.ToLowerInvariant() ?? node.Status;
        node.LastHeartbeatUtc = membership.LastSeenUtc ?? node.LastHeartbeatUtc;
        node.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private async Task<HomeNodeRecord> ClaimNodeAsync(
        HomeNodeRecord node,
        string userId,
        CancellationToken cancellationToken)
    {
        node.OwnerUserId = userId;
        node.AssignedUtc ??= DateTimeOffset.UtcNow;
        node.UpdatedUtc = DateTimeOffset.UtcNow;
        await _homeNodeRepository.UpsertAsync(node, cancellationToken).ConfigureAwait(false);
        return node;
    }

    private static ParsedNodeReferenceParseResult ParseReference(CurrentUserHomeNodeLinkRequestDto request)
    {
        var explicitNodeId = NormalizeNullable(request.NodeId);
        var explicitTailscaleIp = NormalizeNullable(request.TailscaleIp);
        var explicitWatcherNodeId = NormalizeNullable(request.WatcherNodeId);
        var explicitPolicyScopeId = NormalizeNullable(request.PolicyScopeId);

        if (!string.IsNullOrWhiteSpace(explicitNodeId) || !string.IsNullOrWhiteSpace(explicitTailscaleIp))
        {
            return new ParsedNodeReferenceParseResult(
                new ParsedNodeReference(
                    explicitNodeId,
                    explicitTailscaleIp,
                    explicitWatcherNodeId,
                    explicitPolicyScopeId,
                    request.LocalOnly,
                    request.RemoteFallbackAllowed,
                    explicitNodeId ?? explicitTailscaleIp,
                    !string.IsNullOrWhiteSpace(explicitNodeId) ? "node_id" : "tailscale_ip",
                    null),
                null);
        }

        var qrPayload = NormalizeNullable(request.QrCodePayload);
        if (string.IsNullOrWhiteSpace(qrPayload))
        {
            return new ParsedNodeReferenceParseResult(null, null);
        }

        if (TryParseJsonPayload(qrPayload, request, out var parsed, out var parseError))
        {
            return new ParsedNodeReferenceParseResult(parsed, null);
        }

        if (!string.IsNullOrWhiteSpace(parseError))
        {
            return new ParsedNodeReferenceParseResult(null, parseError);
        }

        var rawReference = BuildRawReference(qrPayload, request);
        return rawReference is null
            ? new ParsedNodeReferenceParseResult(
                null,
                "The QR payload must contain a node_id, a tailscale_ip, or the canonical JSON payload from the node.")
            : new ParsedNodeReferenceParseResult(rawReference, null);
    }

    private static bool TryParseJsonPayload(
        string qrPayload,
        CurrentUserHomeNodeLinkRequestDto request,
        out ParsedNodeReference parsed,
        out string? errorMessage)
    {
        parsed = default!;
        errorMessage = null;

        try
        {
            using var document = JsonDocument.Parse(Uri.UnescapeDataString(qrPayload));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            foreach (var candidate in EnumerateJsonPayloadCandidates(root))
            {
                if (!TryBuildReferenceFromJson(candidate, root, request, qrPayload, out parsed))
                {
                    continue;
                }

                return true;
            }

            errorMessage = "The QR payload JSON is missing node_id or tailscale_ip.";
            return false;
        }
        catch (JsonException)
        {
            errorMessage = LooksLikeStructuredPayload(qrPayload)
                ? "The QR payload JSON is malformed. Paste the JSON from /payload.json or scan the node QR code again."
                : null;
            return false;
        }
        catch (UriFormatException)
        {
            errorMessage = "The QR payload could not be decoded.";
            return false;
        }
    }

    private static bool TryBuildReferenceFromJson(
        JsonElement element,
        JsonElement root,
        CurrentUserHomeNodeLinkRequestDto request,
        string qrPayload,
        out ParsedNodeReference parsed)
    {
        parsed = default!;

        var nodeId =
            ReadString(element, "node_id") ??
            ReadString(element, "nodeId") ??
            ReadString(element, "home_node_id") ??
            ReadString(element, "homeNodeId");
        var tailscaleIp =
            ReadString(element, "tailscale_ip") ??
            ReadString(element, "tailscaleIp");
        var identifier = ReadString(element, "identifier");

        if (string.IsNullOrWhiteSpace(nodeId) && string.IsNullOrWhiteSpace(tailscaleIp) && !string.IsNullOrWhiteSpace(identifier))
        {
            if (IPAddress.TryParse(identifier, out _))
            {
                tailscaleIp = identifier;
            }
            else
            {
                nodeId = identifier;
            }
        }

        if (string.IsNullOrWhiteSpace(nodeId) && string.IsNullOrWhiteSpace(tailscaleIp))
        {
            return false;
        }

        var matchedIdentifier = nodeId ?? tailscaleIp ?? identifier ?? qrPayload;
        var matchedVia = !string.IsNullOrWhiteSpace(nodeId)
            ? "node_id"
            : !string.IsNullOrWhiteSpace(tailscaleIp)
                ? "tailscale_ip"
                : "qr_code_payload";

        parsed = new ParsedNodeReference(
            NormalizeNullable(nodeId),
            NormalizeNullable(tailscaleIp),
            NormalizeNullable(ReadString(element, "watcher_node_id")) ??
            NormalizeNullable(ReadString(root, "watcher_node_id")) ??
            NormalizeNullable(request.WatcherNodeId),
            NormalizeNullable(ReadString(element, "policy_scope_id")) ??
            NormalizeNullable(ReadString(root, "policy_scope_id")) ??
            NormalizeNullable(request.PolicyScopeId),
            ReadBoolean(element, "local_only") ??
            ReadBoolean(root, "local_only") ??
            request.LocalOnly,
            ReadBoolean(element, "remote_fallback_allowed") ??
            ReadBoolean(root, "remote_fallback_allowed") ??
            request.RemoteFallbackAllowed,
            matchedIdentifier,
            matchedVia,
            NormalizeNullable(ReadString(element, "tenant_id")) ??
            NormalizeNullable(ReadString(element, "tenantId")) ??
            NormalizeNullable(ReadString(root, "tenant_id")) ??
            NormalizeNullable(ReadString(root, "tenantId")));

        return true;
    }

    private static IEnumerable<JsonElement> EnumerateJsonPayloadCandidates(JsonElement root)
    {
        yield return root;

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "payload", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, "qr_payload", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, "qrPayload", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(property.Name, "node", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                yield return property.Value;
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var nestedJson = NormalizeNullable(property.Value.GetString());
            if (string.IsNullOrWhiteSpace(nestedJson) || !LooksLikeStructuredPayload(nestedJson))
            {
                continue;
            }

            JsonElement? nestedCandidate = null;
            try
            {
                using var nestedDocument = JsonDocument.Parse(nestedJson);
                if (nestedDocument.RootElement.ValueKind == JsonValueKind.Object)
                {
                    nestedCandidate = nestedDocument.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
            }

            if (nestedCandidate.HasValue)
            {
                yield return nestedCandidate.Value;
            }
        }
    }

    private static ParsedNodeReference? BuildRawReference(string qrPayload, CurrentUserHomeNodeLinkRequestDto request)
    {
        var normalized = NormalizeNullable(qrPayload)!;
        if (!LooksLikeLegacyRawIdentifier(normalized))
        {
            return null;
        }

        var isIpAddress = IPAddress.TryParse(normalized, out _);

        return new ParsedNodeReference(
            isIpAddress ? null : normalized,
            isIpAddress ? normalized : null,
            NormalizeNullable(request.WatcherNodeId),
            NormalizeNullable(request.PolicyScopeId),
            request.LocalOnly,
            request.RemoteFallbackAllowed,
            normalized,
            isIpAddress ? "tailscale_ip" : "node_id",
            null);
    }

    private static bool LooksLikeStructuredPayload(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) ||
               trimmed.StartsWith("[", StringComparison.Ordinal) ||
               trimmed.StartsWith("%7B", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("%5B", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLegacyRawIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !value.Any(char.IsWhiteSpace) &&
               !value.Contains('{') &&
               !value.Contains('}') &&
               !value.Contains('[') &&
               !value.Contains(']') &&
               !value.Contains('"') &&
               !value.Contains("://", StringComparison.Ordinal);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind == JsonValueKind.String
                ? NormalizeNullable(property.Value.GetString())
                : null;
        }

        return null;
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return null;
    }

    private static CurrentUserHomeNodeLinkDto BuildResponse(
        string tenantId,
        string userId,
        HomeNodeAssignmentDto? assignment,
        HomeNodeRecord? node,
        string? matchedIdentifier,
        string? matchedVia)
    {
        return new CurrentUserHomeNodeLinkDto
        {
            TenantId = tenantId,
            UserId = userId,
            Linked = assignment is not null,
            MatchedIdentifier = matchedIdentifier,
            MatchedVia = matchedVia,
            Assignment = assignment,
            Node = node is null ? null : MapNode(node)
        };
    }

    private static HomeNodeAssignmentDto MapAssignment(HomeNodeAssignment assignment)
    {
        return new HomeNodeAssignmentDto
        {
            TenantId = assignment.TenantId,
            UserId = assignment.UserId,
            PrimaryHomeNodeId = assignment.PrimaryHomeNodeId,
            WatcherNodeId = assignment.WatcherNodeId,
            PolicyScopeId = assignment.PolicyScopeId,
            LocalOnly = assignment.LocalOnly,
            RemoteFallbackAllowed = assignment.RemoteFallbackAllowed,
            Status = assignment.Status,
            AssignedUtc = assignment.AssignedUtc,
            RevokedUtc = assignment.RevokedUtc,
            UpdatedUtc = assignment.UpdatedUtc
        };
    }

    private static HomeNodeDto MapNode(HomeNodeRecord record)
    {
        return new HomeNodeDto
        {
            TenantId = record.TenantId,
            NodeId = record.NodeId,
            OwnerUserId = record.OwnerUserId,
            DeviceClass = record.DeviceClass,
            TailscaleIp = record.TailscaleIp,
            WatcherNodeId = record.WatcherNodeId,
            Capabilities = string.IsNullOrWhiteSpace(record.CapabilitiesJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(record.CapabilitiesJson) ?? new Dictionary<string, object?>(),
            PackVersions = string.IsNullOrWhiteSpace(record.PackVersionsJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(record.PackVersionsJson) ?? new Dictionary<string, object?>(),
            PolicyProfileVersion = record.PolicyProfileVersion,
            Status = record.Status,
            LastHeartbeatUtc = record.LastHeartbeatUtc,
            AssignedUtc = record.AssignedUtc,
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc
        };
    }

    private static string SerializeMap(IReadOnlyDictionary<string, object?>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), JsonOptions);
    }

    private static bool IsUnclaimedOwner(string? ownerUserId)
    {
        return string.IsNullOrWhiteSpace(ownerUserId) ||
               string.Equals(ownerUserId.Trim(), "unclaimed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ownerUserId.Trim(), "pending", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveAssignment(HomeNodeAssignment? assignment)
    {
        return assignment is not null &&
               string.Equals(assignment.Status, "active", StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceResponse<CurrentUserHomeNodeLinkDto> Fail(string message, int statusCode)
    {
        return ServiceResponse<CurrentUserHomeNodeLinkDto>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode,
            message);
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record ParsedNodeReference(
        string? NodeId,
        string? TailscaleIp,
        string? WatcherNodeId,
        string? PolicyScopeId,
        bool? LocalOnly,
        bool? RemoteFallbackAllowed,
        string? MatchedIdentifier,
        string? MatchedVia,
        string? TenantId);

    private sealed record ParsedNodeReferenceParseResult(
        ParsedNodeReference? Reference,
        string? ErrorMessage);
}
#nullable disable
