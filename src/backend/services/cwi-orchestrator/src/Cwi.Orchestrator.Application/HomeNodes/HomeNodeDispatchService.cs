#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeDispatchService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHomeNodeAssignmentRepository _assignmentRepository;
    private readonly IHomeNodeRepository _homeNodeRepository;
    private readonly IHomeNodePolicyScopeRepository _policyScopeRepository;
    private readonly HomeNodeDispatchClient _dispatchClient;
    private readonly HomeNodeWorkTokenService _tokenService;
    private readonly JourneyEmitter _journeyEmitter;
    private readonly ApplicationOptions _options;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;
    private readonly ILogger<HomeNodeDispatchService> _logger;

    public HomeNodeDispatchService(
        IHomeNodeAssignmentRepository assignmentRepository,
        IHomeNodeRepository homeNodeRepository,
        IHomeNodePolicyScopeRepository policyScopeRepository,
        HomeNodeDispatchClient dispatchClient,
        HomeNodeWorkTokenService tokenService,
        JourneyEmitter journeyEmitter,
        ApplicationOptions options,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory,
        ILogger<HomeNodeDispatchService> logger)
    {
        _assignmentRepository = assignmentRepository;
        _homeNodeRepository = homeNodeRepository;
        _policyScopeRepository = policyScopeRepository;
        _dispatchClient = dispatchClient;
        _tokenService = tokenService;
        _journeyEmitter = journeyEmitter;
        _options = options;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
        _logger = logger;
    }

    public Task<ServiceResponse<HomeNodeAskDispatchDecision>> TryDispatchAskAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleAsync(context, cancellationToken));
    }

    private async Task<ServiceResponse<HomeNodeAskDispatchDecision>> HandleAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId))
        {
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision());
        }

        var assignment = await _assignmentRepository.GetAsync(context.TenantId, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null || !string.Equals(assignment.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision());
        }

        var node = await _homeNodeRepository.GetAsync(context.TenantId, assignment.PrimaryHomeNodeId, cancellationToken)
            .ConfigureAwait(false);
        var policyScope = await GetPolicyScopeAsync(context.TenantId, assignment.PolicyScopeId, cancellationToken)
            .ConfigureAwait(false);
        ApplyHomeNodeRouteContext(context, assignment, node, null, node?.Status);

        if (ShouldPreferSharedComputeForGovernedQuery(context))
        {
            const string routeStatus = "skipped_for_governed_query";
            var routeReason = ResolveSharedComputeRouteReason(context);
            context.HomeNodeStatus = routeStatus;

            await AppendLedgerEventAsync(context, "home_node.dispatch_skipped", new
            {
                node_id = assignment.PrimaryHomeNodeId,
                policy_scope_id = assignment.PolicyScopeId,
                reason = routeReason,
                query_classification = context.QueryClassification
            }, cancellationToken).ConfigureAwait(false);
            await EmitHomeNodeJourneyAsync(
                context,
                "home_node_dispatch_skipped",
                "request",
                HomeNodeComponent(assignment.PrimaryHomeNodeId),
                "cwi-orchestrator",
                "Query retained on shared compute.",
                BuildRouteMetadata(assignment, node, null, routeStatus, null, routeReason),
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
            {
                ContinueSharedPath = true
            });
        }

        if (PolicyBlocksDispatch(policyScope))
        {
            ApplyTerminalExecutionState(
                context,
                "denied",
                "home_node",
                node?.Status ?? "policy_blocked",
                "Local execution is not permitted by the assigned home-node policy.",
                "Assigned home-node policy denied this request.");
            await AppendLedgerEventAsync(context, "home_node.dispatch_blocked", new
            {
                node_id = assignment.PrimaryHomeNodeId,
                policy_scope_id = assignment.PolicyScopeId,
                reason = "policy_blocked"
            }, cancellationToken).ConfigureAwait(false);
            await EmitHomeNodeJourneyAsync(
                context,
                "home_node_dispatch_blocked",
                "response",
                HomeNodeComponent(assignment.PrimaryHomeNodeId),
                "cwi-orchestrator",
                "Assigned home-node policy denied this request.",
                BuildRouteMetadata(assignment, node, null, "policy_blocked", null, "policy_blocked"),
                cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
            {
                ContinueSharedPath = false
            });
        }

        var fallbackAllowed = AllowsSharedFallback(assignment, policyScope);
        if (node is null)
        {
            return await HandleUnavailableNodeAsync(
                    context,
                    assignment,
                    fallbackAllowed,
                    "unregistered",
                    "Assigned home node is not registered in the control plane.",
                    null,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var registryAvailability = EvaluateRegistryAvailability(node);
        if (!registryAvailability.Dispatchable)
        {
            return await HandleUnavailableNodeAsync(
                    context,
                    assignment,
                    fallbackAllowed,
                    registryAvailability.Status,
                    $"Assigned home node is {registryAvailability.Status}.",
                    node,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!_dispatchClient.IsConfigured || !_tokenService.IsConfigured)
        {
            return await HandleUnavailableNodeAsync(
                    context,
                    assignment,
                    fallbackAllowed,
                    "dispatch_unavailable",
                    "Home-node dispatch is not configured in the control plane.",
                    node,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var membershipLookup = await _dispatchClient.GetMembershipAsync(node.NodeId, cancellationToken)
            .ConfigureAwait(false);
        if (!membershipLookup.IsSuccess)
        {
            _logger.LogWarning(
                "Home-node membership lookup failed for tenant {TenantId} user {UserId} node {NodeId}: {Message}",
                context.TenantId,
                context.UserId,
                node.NodeId,
                membershipLookup.Message);

            return await HandleUnavailableNodeAsync(
                    context,
                    assignment,
                    fallbackAllowed,
                    "membership_unavailable",
                    "Home-node live membership could not be resolved from coordination.",
                    node,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var membership = membershipLookup.Data;
        var nodeAvailability = EvaluateAvailability(node, membership);
        ApplyHomeNodeRouteContext(context, assignment, node, membership, nodeAvailability.Status);
        if (!nodeAvailability.Dispatchable)
        {
            return await HandleUnavailableNodeAsync(
                    context,
                    assignment,
                    fallbackAllowed,
                    nodeAvailability.Status,
                    $"Assigned home node is {nodeAvailability.Status}.",
                    node,
                    membership,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var modelRoute = ResolveModelRoute(context, membership, fallbackAllowed);
        if (!modelRoute.UseHomeNode)
        {
            const string routeStatus = "skipped_for_model_route";
            context.HomeNodeStatus = routeStatus;
            context.ExecutionTarget = "shared_compute";
            context.ExecutionMessage = "Request routed to rack/core because the linked home node profile is not the best fit.";

            await AppendLedgerEventAsync(context, "home_node.dispatch_skipped", new
            {
                node_id = node.NodeId,
                policy_scope_id = assignment.PolicyScopeId,
                reason = modelRoute.Reason,
                model_route = modelRoute.ToMetadata(),
                fallback_allowed = fallbackAllowed
            }, cancellationToken).ConfigureAwait(false);
            await EmitHomeNodeJourneyAsync(
                context,
                "home_node_dispatch_skipped",
                "request",
                HomeNodeComponent(node.NodeId),
                "rack-core",
                "Request routed to rack/core by home-node model policy.",
                BuildRouteMetadata(assignment, node, membership, routeStatus, null, modelRoute.Reason, modelRoute),
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
            {
                ContinueSharedPath = true
            });
        }

        await EmitHomeNodeJourneyAsync(
            context,
            "home_node_route_selected",
            "request",
            "cwi-orchestrator",
            HomeNodeComponent(node.NodeId),
            "Assigned linked home node selected for this request.",
            BuildRouteMetadata(assignment, node, membership, nodeAvailability.Status, null, null, modelRoute),
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var workId = Guid.NewGuid().ToString("N");
        var token = _tokenService.CreateAskToken(
            workId,
            context.RequestId,
            context.TraceId,
            context.TenantId,
            context.UserId,
            node.NodeId,
            assignment.PolicyScopeId,
            context.PackName);

        await AppendLedgerEventAsync(context, "home_node.dispatch_requested", new
        {
            work_id = workId,
            node_id = node.NodeId,
            membership_status = membership?.Status,
            membership_last_seen_utc = membership?.LastSeenUtc,
            policy_scope_id = assignment.PolicyScopeId,
            fallback_allowed = fallbackAllowed,
            model_route = modelRoute.ToMetadata()
        }, cancellationToken).ConfigureAwait(false);
        await EmitHomeNodeJourneyAsync(
            context,
            "home_node_dispatch_requested",
            "request",
            "cwi-orchestrator",
            HomeNodeComponent(node.NodeId),
            "Request forwarded to linked home node through coordination.",
            BuildRouteMetadata(assignment, node, membership, nodeAvailability.Status, workId, null, modelRoute),
            cancellationToken).ConfigureAwait(false);

        var dispatch = await _dispatchClient.CreateAsync(
            new CreateHomeNodeWorkRequest
            {
                WorkId = workId,
                RequestId = context.RequestId,
                TraceId = context.TraceId,
                NodeId = node.NodeId,
                TenantId = context.TenantId,
                UserId = context.UserId,
                PolicyScope = assignment.PolicyScopeId,
                Payload = BuildPayload(context, assignment, policyScope, node, modelRoute),
                WorkToken = token,
                IdempotencyKey = BuildIdempotencyKey(context, node.NodeId)
            },
            cancellationToken).ConfigureAwait(false);

        if (!dispatch.IsSuccess || dispatch.Data is null)
        {
            _logger.LogWarning(
                "Home-node dispatch failed for tenant {TenantId} user {UserId} node {NodeId}: {Message}",
                context.TenantId,
                context.UserId,
                node.NodeId,
                dispatch.Message);

            if (fallbackAllowed)
            {
                ApplyFallbackExecutionState(context, "dispatch_failed");
                await AppendLedgerEventAsync(context, "home_node.dispatch_fallback", new
                {
                    node_id = node.NodeId,
                    reason = "dispatch_failed",
                    shared_fallback = true
                }, cancellationToken).ConfigureAwait(false);
                await EmitHomeNodeJourneyAsync(
                    context,
                    "home_node_dispatch_fallback",
                    "response",
                    HomeNodeComponent(node.NodeId),
                    "shared-compute",
                    "Home-node dispatch failed; shared compute fallback will continue.",
                    BuildRouteMetadata(assignment, node, membership, "dispatch_failed", workId, "dispatch_failed"),
                    cancellationToken).ConfigureAwait(false);
                return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision());
            }

            ApplyTerminalExecutionState(
                context,
                "degraded",
                "home_node",
                "dispatch_failed",
                "Your assigned home node could not accept the request and shared fallback is disabled.",
                "Home-node dispatch failed and the request remained local-only.");
            await AppendLedgerEventAsync(context, "home_node.dispatch_blocked", new
            {
                node_id = node.NodeId,
                reason = "dispatch_failed",
                shared_fallback = false
            }, cancellationToken).ConfigureAwait(false);
            await EmitHomeNodeJourneyAsync(
                context,
                "home_node_dispatch_blocked",
                "response",
                HomeNodeComponent(node.NodeId),
                "cwi-orchestrator",
                "Home-node dispatch failed and shared fallback is disabled.",
                BuildRouteMetadata(assignment, node, membership, "dispatch_failed", workId, "dispatch_failed"),
                cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
            {
                ContinueSharedPath = false
            });
        }

        context.Answer = "Request forwarded to your assigned home node for local execution.";
        context.ExecutionState = "pending";
        context.ExecutionTarget = "home_node";
        context.DeferredWorkId = dispatch.Data.WorkId;
        context.HomeNodeStatus = nodeAvailability.Status;
        context.ExecutionMessage = "Home node accepted the request for local execution.";
        context.ModelName = "home-node";

        await AppendLedgerEventAsync(context, "home_node.dispatch_queued", new
        {
            work_id = dispatch.Data.WorkId,
            node_id = node.NodeId,
            node_status = nodeAvailability.Status,
            membership_status = membership?.Status,
            membership_last_seen_utc = membership?.LastSeenUtc,
            queue_status = dispatch.Data.Status,
            model_route = modelRoute.ToMetadata()
        }, cancellationToken).ConfigureAwait(false);
        await EmitHomeNodeJourneyAsync(
            context,
            "home_node_work_queued",
            "request",
            "cwi-orchestrator",
            HomeNodeComponent(node.NodeId),
            "Linked home node accepted queued local work.",
            BuildRouteMetadata(assignment, node, membership, nodeAvailability.Status, dispatch.Data.WorkId, dispatch.Data.Status, modelRoute),
            cancellationToken).ConfigureAwait(false);

        var completedInline = await TryAwaitTerminalResultAsync(
                context,
                dispatch.Data.WorkId,
                fallbackAllowed,
                assignment,
                node,
                membership,
                cancellationToken)
            .ConfigureAwait(false);
        if (completedInline.HasValue)
        {
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
            {
                ContinueSharedPath = completedInline.Value
            });
        }

        return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
        {
            ContinueSharedPath = false
        });
    }

    private async Task<HomeNodePolicyScope?> GetPolicyScopeAsync(
        string tenantId,
        string? policyScopeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(policyScopeId))
        {
            return null;
        }

        return await _policyScopeRepository.GetAsync(tenantId, policyScopeId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServiceResponse<HomeNodeAskDispatchDecision>> HandleUnavailableNodeAsync(
        AskWorkflowContext context,
        HomeNodeAssignment assignment,
        bool fallbackAllowed,
        string status,
        string message,
        HomeNodeRecord? node,
        HomeNodeMembershipSnapshot? membership,
        CancellationToken cancellationToken)
    {
        ApplyHomeNodeRouteContext(context, assignment, node, membership, status);

        if (fallbackAllowed)
        {
            ApplyFallbackExecutionState(context, status);
            await AppendLedgerEventAsync(context, "home_node.dispatch_fallback", new
            {
                node_id = assignment.PrimaryHomeNodeId,
                membership_status = membership?.Status,
                membership_last_seen_utc = membership?.LastSeenUtc,
                reason = status,
                shared_fallback = true
            }, cancellationToken).ConfigureAwait(false);
            await EmitHomeNodeJourneyAsync(
                context,
                "home_node_dispatch_fallback",
                "response",
                HomeNodeComponent(assignment.PrimaryHomeNodeId),
                "shared-compute",
                message,
                BuildRouteMetadata(assignment, node, membership, status, null, status),
                cancellationToken).ConfigureAwait(false);
            return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision());
        }

        ApplyTerminalExecutionState(
            context,
            "offline",
            "home_node",
            status,
            "Your assigned home node is unavailable and this request cannot leave the local execution boundary.",
            message);
        await AppendLedgerEventAsync(context, "home_node.dispatch_blocked", new
        {
            node_id = assignment.PrimaryHomeNodeId,
            membership_status = membership?.Status,
            membership_last_seen_utc = membership?.LastSeenUtc,
            reason = status,
            shared_fallback = false
        }, cancellationToken).ConfigureAwait(false);
        await EmitHomeNodeJourneyAsync(
            context,
            "home_node_dispatch_blocked",
            "response",
            HomeNodeComponent(assignment.PrimaryHomeNodeId),
            "cwi-orchestrator",
            message,
            BuildRouteMetadata(assignment, node, membership, status, null, status),
            cancellationToken).ConfigureAwait(false);
        return ServiceResponse<HomeNodeAskDispatchDecision>.Success(new HomeNodeAskDispatchDecision
        {
            ContinueSharedPath = false
        });
    }

    private Dictionary<string, object?> BuildPayload(
        AskWorkflowContext context,
        HomeNodeAssignment assignment,
        HomeNodePolicyScope? policyScope,
        HomeNodeRecord node,
        HomeNodeModelRoute modelRoute)
    {
        return new Dictionary<string, object?>
        {
            ["request_kind"] = "ask",
            ["question"] = context.RawQuestion,
            ["normalized_question"] = context.RoutingQuestion,
            ["tenant_id"] = context.TenantId,
            ["user_id"] = context.UserId,
            ["pack"] = new Dictionary<string, object?>
            {
                ["name"] = context.PackName,
                ["hash"] = context.PackHash,
                ["version"] = context.PackVersion,
                ["requested_anchor"] = context.RequestedPackAnchor
            },
            ["policy"] = new Dictionary<string, object?>
            {
                ["mode"] = context.PolicyMode,
                ["version"] = context.PolicyVersion,
                ["scope_id"] = assignment.PolicyScopeId,
                ["rules"] = DeserializeRules(policyScope),
                ["local_only"] = assignment.LocalOnly,
                ["remote_fallback_allowed"] = assignment.RemoteFallbackAllowed
            },
            ["execution"] = new Dictionary<string, object?>
            {
                ["model_tier"] = context.ModelTier,
                ["target_node_id"] = node.NodeId,
                ["policy_profile_version"] = node.PolicyProfileVersion,
                ["route_mode"] = modelRoute.Mode,
                ["route_reason"] = modelRoute.Reason,
                ["use_local_model"] = string.Equals(modelRoute.Mode, "local_model", StringComparison.OrdinalIgnoreCase),
                ["model_profile"] = modelRoute.ModelProfile,
                ["max_context_tokens"] = modelRoute.MaxContextTokens,
                ["pack_guidance_mode"] = "compact_cached",
                ["retrieval_mode"] = "relevant_chunks_only",
                ["heavy_reasoning_target"] = "rack_core"
            },
            ["operator_profile"] = context.OperatorProfile,
            ["operator_profile_decision"] = context.OperatorProfileDecision,
            ["requested_utc"] = DateTimeOffset.UtcNow
        };
    }

    private string BuildIdempotencyKey(AskWorkflowContext context, string nodeId)
    {
        return $"ask:{context.TenantId}:{context.UserId}:{nodeId}:{context.RequestId ?? context.TraceId ?? Guid.NewGuid().ToString("N")}";
    }

    private bool AllowsSharedFallback(HomeNodeAssignment assignment, HomeNodePolicyScope? policyScope)
    {
        if (assignment.LocalOnly)
        {
            return false;
        }

        var rules = DeserializeRules(policyScope);
        var remoteFallbackAllowed = assignment.RemoteFallbackAllowed;
        var ruleOverride = TryGetBoolean(rules, "remote_fallback_allowed");
        if (ruleOverride.HasValue)
        {
            remoteFallbackAllowed = ruleOverride.Value;
        }

        if (TryGetBoolean(rules, "local_execution_required") == true ||
            TryGetBoolean(rules, "local_only") == true)
        {
            return false;
        }

        return remoteFallbackAllowed;
    }

    private static bool ShouldPreferSharedComputeForGovernedQuery(AskWorkflowContext context)
    {
        return context.SkipModelInference
            || (context.EvidenceRequired
                && string.Equals(context.GroundingMode, "source_grounded", StringComparison.OrdinalIgnoreCase))
            || string.Equals(context.QueryClassification, "system_truth", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "preference_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "controlled_fact_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "controlled_fact_lookup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "memory_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSharedComputeRouteReason(AskWorkflowContext context)
    {
        return context.QueryClassification switch
        {
            "preference_update" => "operator_memory_update",
            "self_context_update" => "governed_memory_update",
            "controlled_fact_update" => "controlled_fact_memory_update",
            "controlled_fact_lookup" => "controlled_fact_memory_recall",
            "deterministic_reasoning" => "deterministic_reasoning",
            _ => "governed_system_truth_query"
        };
    }

    private static HomeNodeModelRoute ResolveModelRoute(
        AskWorkflowContext context,
        HomeNodeMembershipSnapshot? membership,
        bool fallbackAllowed)
    {
        var capabilities = membership?.Capabilities ?? new Dictionary<string, object?>();
        var health = membership?.Health ?? new Dictionary<string, object?>();
        var profile = TryGetObject(capabilities, "local_model_profile") ?? TryGetObject(health, "model_profile");
        var hardware = TryGetObject(health, "hardware") ?? TryGetObject(capabilities, "hardware");
        var hasLocalModel = TryGetBoolean(capabilities, "local_model") == true || profile is not null;
        var maxVramGb = TryGetProfileDouble(profile, "max_vram_gb")
            ?? TryGetProfileDouble(hardware, "gpu_vram_gb")
            ?? TryGetProfileDouble(hardware, "configured_gpu_vram_gb");
        var heavyReasoning = IsHeavyReasoningRequest(context);

        if (heavyReasoning && fallbackAllowed)
        {
            return new HomeNodeModelRoute(
                UseHomeNode: false,
                Mode: "shared_compute",
                Reason: "heavy_reasoning_route_to_rack_core",
                MaxContextTokens: null,
                GpuVramGb: maxVramGb,
                ModelProfile: BuildRouteModelProfile(profile, null, maxVramGb));
        }

        if (hasLocalModel)
        {
            var configuredContextTokens = TryGetProfileInt(profile, "context_tokens") ?? 4096;
            var maxContextTokens = maxVramGb is <= 6.0
                ? Math.Min(configuredContextTokens, 4096)
                : Math.Min(configuredContextTokens, 8192);
            var reason = heavyReasoning
                ? "local_only_heavy_reasoning_limited_home_node"
                : maxVramGb is <= 6.0
                    ? "small_quantized_home_node_profile"
                    : "local_model_profile";

            return new HomeNodeModelRoute(
                UseHomeNode: true,
                Mode: "local_model",
                Reason: reason,
                MaxContextTokens: maxContextTokens,
                GpuVramGb: maxVramGb,
                ModelProfile: BuildRouteModelProfile(profile, maxContextTokens, maxVramGb));
        }

        return new HomeNodeModelRoute(
            UseHomeNode: true,
            Mode: "local_artifact_search",
            Reason: heavyReasoning ? "local_only_no_model_profile" : "local_artifact_search",
            MaxContextTokens: null,
            GpuVramGb: maxVramGb,
            ModelProfile: BuildRouteModelProfile(profile, null, maxVramGb));
    }

    private static bool IsHeavyReasoningRequest(AskWorkflowContext context)
    {
        if (context.RoutingQuestion.Length > 1200 || context.TopK > 8)
        {
            return true;
        }

        if (context.DomainPackNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            return true;
        }

        if (context.PackCapabilities?.Capabilities.Any(capability =>
                capability.RequiresApproval || capability.Risk == RiskLevel.High) == true)
        {
            return true;
        }

        var question = context.RoutingQuestion.ToLowerInvariant();
        string[] heavyMarkers =
        {
            "heavy reasoning",
            "multi-pack",
            "synthesis",
            "synthesize",
            "compare options",
            "risk analysis",
            "risky shortcut",
            "production",
            "architecture",
            "timeout",
            "latency",
        };

        return heavyMarkers.Any(marker => question.Contains(marker, StringComparison.Ordinal));
    }

    private static Dictionary<string, object?> BuildRouteModelProfile(
        IReadOnlyDictionary<string, object?>? profile,
        int? maxContextTokens,
        double? maxVramGb)
    {
        return new Dictionary<string, object?>
        {
            ["provider"] = profile is null ? null : TryGetString(profile, "provider"),
            ["model"] = profile is null ? null : (TryGetString(profile, "model") ?? TryGetString(profile, "model_name")),
            ["model_family"] = profile is null ? null : TryGetString(profile, "model_family"),
            ["quantization"] = profile is null ? null : TryGetString(profile, "quantization"),
            ["configured_context_tokens"] = profile is null ? null : TryGetInt(profile, "context_tokens"),
            ["max_context_tokens"] = maxContextTokens,
            ["max_vram_gb"] = maxVramGb
        };
    }

    private static int? TryGetProfileInt(IReadOnlyDictionary<string, object?>? values, string key)
    {
        return values is null ? null : TryGetInt(values, key);
    }

    private static double? TryGetProfileDouble(IReadOnlyDictionary<string, object?>? values, string key)
    {
        return values is null ? null : TryGetDouble(values, key);
    }

    private static bool PolicyBlocksDispatch(HomeNodePolicyScope? policyScope)
    {
        var rules = DeserializeRules(policyScope);
        return TryGetBoolean(rules, "dispatch_enabled") == false ||
               TryGetBoolean(rules, "allow_dispatch") == false ||
               TryGetBoolean(rules, "allow_ask_dispatch") == false ||
               TryGetBoolean(rules, "allow_execution") == false;
    }

    private static NodeAvailability EvaluateRegistryAvailability(HomeNodeRecord node)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(node.Status)
            ? "unknown"
            : node.Status.Trim().ToLowerInvariant();

        if (normalizedStatus is "offline" or "revoked" or "disabled" or "denied")
        {
            return new NodeAvailability(false, normalizedStatus);
        }

        return new NodeAvailability(true, normalizedStatus);
    }

    private NodeAvailability EvaluateAvailability(
        HomeNodeRecord node,
        HomeNodeMembershipSnapshot? membership)
    {
        var registryAvailability = EvaluateRegistryAvailability(node);
        if (!registryAvailability.Dispatchable)
        {
            return registryAvailability;
        }

        if (membership is null)
        {
            return new NodeAvailability(false, "not_connected");
        }

        var membershipStatus = string.IsNullOrWhiteSpace(membership.Status)
            ? "unknown"
            : membership.Status.Trim().ToLowerInvariant();
        if (membershipStatus is "offline" or "revoked" or "disabled" or "denied")
        {
            return new NodeAvailability(false, membershipStatus);
        }

        if (membership.LastSeenUtc is null)
        {
            return new NodeAvailability(false, "unseen");
        }

        if (membership.LastSeenUtc is not null)
        {
            var maxAge = TimeSpan.FromSeconds(_options.HomeNodeHeartbeatMaxAgeSeconds);
            if (DateTimeOffset.UtcNow - membership.LastSeenUtc.Value > maxAge)
            {
                return new NodeAvailability(false, "stale");
            }
        }

        var healthStatus = TryGetString(membership.Health, "status")?.Trim().ToLowerInvariant();
        if (healthStatus is "offline" or "revoked" or "disabled" or "denied" or "unready")
        {
            return new NodeAvailability(false, healthStatus);
        }

        if (membershipStatus is "active" or "ready" or "healthy" or "assigned" or "connected")
        {
            return new NodeAvailability(true, membershipStatus);
        }

        return new NodeAvailability(false, membershipStatus);
    }

    private static Dictionary<string, object?> DeserializeRules(HomeNodePolicyScope? policyScope)
    {
        if (policyScope is null || string.IsNullOrWhiteSpace(policyScope.RulesJson))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(policyScope.RulesJson, JsonOptions)
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, object?> rules, string key)
    {
        if (!rules.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            bool direct => direct,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            JsonElement element when element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsedFromString) => parsedFromString,
            string text when bool.TryParse(text, out var parsedText) => parsedText,
            _ => null
        };
    }

    private static void ApplyFallbackExecutionState(AskWorkflowContext context, string homeNodeStatus)
    {
        context.HomeNodeStatus = homeNodeStatus;
        context.ExecutionTarget = "shared_compute";
        context.ExecutionState = "completed";
        context.ExecutionMessage = "Shared compute fallback continued after local execution was unavailable.";
        context.FallbackApplied = true;
    }

    private static void ApplyTerminalExecutionState(
        AskWorkflowContext context,
        string executionState,
        string executionTarget,
        string homeNodeStatus,
        string answer,
        string executionMessage)
    {
        context.Answer = answer;
        context.ExecutionState = executionState;
        context.ExecutionTarget = executionTarget;
        context.HomeNodeStatus = homeNodeStatus;
        context.ExecutionMessage = executionMessage;
        context.ModelName = "home-node";
    }

    private static void ApplyHomeNodeRouteContext(
        AskWorkflowContext context,
        HomeNodeAssignment assignment,
        HomeNodeRecord? node,
        HomeNodeMembershipSnapshot? membership,
        string? status)
    {
        context.HomeNodeId = node?.NodeId ?? assignment.PrimaryHomeNodeId;
        context.HomeNodeOwnerUserId = ResolveOwnerUserId(node, membership);
        context.HomeNodeTailscaleIp = ResolveTailscaleIp(node, membership);
        context.HomeNodeMembershipStatus = membership?.Status;
        context.HomeNodeMembershipLastSeenUtc = membership?.LastSeenUtc;

        if (!string.IsNullOrWhiteSpace(status))
        {
            context.HomeNodeStatus = status;
        }
    }

    private static Dictionary<string, object?> BuildRouteMetadata(
        HomeNodeAssignment assignment,
        HomeNodeRecord? node,
        HomeNodeMembershipSnapshot? membership,
        string status,
        string? workId,
        string? reasonOrQueueStatus,
        HomeNodeModelRoute? modelRoute = null)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["home_node_id"] = node?.NodeId ?? assignment.PrimaryHomeNodeId,
            ["primary_home_node_id"] = assignment.PrimaryHomeNodeId,
            ["owner_user_id"] = ResolveOwnerUserId(node, membership),
            ["tailscale_ip"] = ResolveTailscaleIp(node, membership),
            ["policy_scope_id"] = assignment.PolicyScopeId,
            ["local_only"] = assignment.LocalOnly,
            ["remote_fallback_allowed"] = assignment.RemoteFallbackAllowed,
            ["registry_status"] = node?.Status,
            ["route_status"] = status,
            ["membership_status"] = membership?.Status,
            ["membership_last_seen_utc"] = membership?.LastSeenUtc,
            ["membership_health"] = membership?.Health,
            ["work_id"] = workId,
            ["detail"] = reasonOrQueueStatus
        };

        if (modelRoute is not null)
        {
            metadata["model_route"] = modelRoute.ToMetadata();
        }

        return metadata;
    }

    private static string? ResolveOwnerUserId(HomeNodeRecord? node, HomeNodeMembershipSnapshot? membership)
    {
        return !string.IsNullOrWhiteSpace(membership?.OwnerUserId)
            ? membership.OwnerUserId
            : node?.OwnerUserId;
    }

    private static string? ResolveTailscaleIp(HomeNodeRecord? node, HomeNodeMembershipSnapshot? membership)
    {
        return node is not null && !string.IsNullOrWhiteSpace(node.TailscaleIp)
            ? node.TailscaleIp
            : membership?.TailscaleIp;
    }

    private Task EmitHomeNodeJourneyAsync(
        AskWorkflowContext context,
        string phase,
        string direction,
        string fromComponent,
        string toComponent,
        string message,
        Dictionary<string, object?> metadata,
        CancellationToken cancellationToken)
    {
        return _journeyEmitter.EmitAsync(
            "home-node-dispatch",
            phase,
            direction,
            fromComponent,
            toComponent,
            message: message,
            metadata: metadata,
            cancellationToken: cancellationToken);
    }

    private static string HomeNodeComponent(string? nodeId)
    {
        return string.IsNullOrWhiteSpace(nodeId)
            ? "home-node:unresolved"
            : $"home-node:{nodeId.Trim()}";
    }

    private async Task AppendLedgerEventAsync(
        AskWorkflowContext context,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var prevHash = await _ledgerRepository.GetLatestHashAsync(context.TraceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            context.TraceId,
            eventType,
            JsonSerializer.Serialize(payload, JsonOptions),
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool?> TryAwaitTerminalResultAsync(
        AskWorkflowContext context,
        string workId,
        bool fallbackAllowed,
        HomeNodeAssignment assignment,
        HomeNodeRecord node,
        HomeNodeMembershipSnapshot? membership,
        CancellationToken cancellationToken)
    {
        var nodeId = node.NodeId;
        var deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(_options.HomeNodeImmediateWaitTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadlineUtc)
        {
            var status = await _dispatchClient.GetAsync(workId, cancellationToken).ConfigureAwait(false);
            if (!status.IsSuccess || status.Data is null)
            {
                _logger.LogWarning(
                    "Home-node work status lookup failed for work {WorkId}: {Message}",
                    workId,
                    status.Message);
                return null;
            }

            var state = status.Data.Status.Trim().ToLowerInvariant();
            switch (state)
            {
                case "completed":
                    ApplyCompletedResult(context, status.Data, nodeId);
                    if (fallbackAllowed && !HasLocalEvidence(status.Data.Result))
                    {
                        ApplyFallbackExecutionState(context, "no_local_evidence");
                        context.Answer = string.Empty;
                        await AppendLedgerEventAsync(context, "home_node.dispatch_fallback", new
                        {
                            work_id = workId,
                            node_id = nodeId,
                            membership_status = membership?.Status,
                            membership_last_seen_utc = membership?.LastSeenUtc,
                            reason = "no_local_evidence",
                            shared_fallback = true
                        }, cancellationToken).ConfigureAwait(false);
                        await EmitHomeNodeJourneyAsync(
                            context,
                            "home_node_dispatch_fallback",
                            "response",
                            HomeNodeComponent(nodeId),
                            "shared-compute",
                            "Linked home node had no matching local evidence; shared compute fallback will continue.",
                            BuildRouteMetadata(assignment, node, membership, "no_local_evidence", workId, "completed"),
                            cancellationToken).ConfigureAwait(false);
                        return true;
                    }
                    await AppendLedgerEventAsync(context, "home_node.dispatch_completed", new
                    {
                        work_id = workId,
                        node_id = nodeId,
                        membership_status = membership?.Status,
                        membership_last_seen_utc = membership?.LastSeenUtc,
                        completed_utc = status.Data.CompletedUtc
                    }, cancellationToken).ConfigureAwait(false);
                    await EmitHomeNodeJourneyAsync(
                        context,
                        "home_node_work_completed",
                        "response",
                        HomeNodeComponent(nodeId),
                        "cwi-orchestrator",
                        "Linked home node completed local execution.",
                        BuildRouteMetadata(assignment, node, membership, "completed", workId, "completed"),
                        cancellationToken).ConfigureAwait(false);
                    return false;

                case "denied":
                    if (fallbackAllowed)
                    {
                        ApplyFallbackExecutionState(context, "denied");
                        await AppendLedgerEventAsync(context, "home_node.dispatch_fallback", new
                        {
                            work_id = workId,
                            node_id = nodeId,
                            membership_status = membership?.Status,
                            membership_last_seen_utc = membership?.LastSeenUtc,
                            reason = "denied",
                            shared_fallback = true
                        }, cancellationToken).ConfigureAwait(false);
                        await EmitHomeNodeJourneyAsync(
                            context,
                            "home_node_dispatch_fallback",
                            "response",
                            HomeNodeComponent(nodeId),
                            "shared-compute",
                            "Linked home node denied local execution; shared compute fallback will continue.",
                            BuildRouteMetadata(assignment, node, membership, "denied", workId, "denied"),
                            cancellationToken).ConfigureAwait(false);
                        return true;
                    }

                    ApplyTerminalExecutionState(
                        context,
                        "denied",
                        "home_node",
                        "denied",
                        "Your assigned home node rejected this request under local policy.",
                        BuildFailureMessage(status.Data.Result, "Home node denied local execution."));
                    await AppendLedgerEventAsync(context, "home_node.dispatch_blocked", new
                    {
                        work_id = workId,
                        node_id = nodeId,
                        membership_status = membership?.Status,
                        membership_last_seen_utc = membership?.LastSeenUtc,
                        reason = "denied",
                        result = status.Data.Result
                    }, cancellationToken).ConfigureAwait(false);
                    await EmitHomeNodeJourneyAsync(
                        context,
                        "home_node_dispatch_blocked",
                        "response",
                        HomeNodeComponent(nodeId),
                        "cwi-orchestrator",
                        "Linked home node denied local execution.",
                        BuildRouteMetadata(assignment, node, membership, "denied", workId, "denied"),
                        cancellationToken).ConfigureAwait(false);
                    return false;

                case "failed":
                    if (fallbackAllowed)
                    {
                        ApplyFallbackExecutionState(context, "failed");
                        await AppendLedgerEventAsync(context, "home_node.dispatch_fallback", new
                        {
                            work_id = workId,
                            node_id = nodeId,
                            membership_status = membership?.Status,
                            membership_last_seen_utc = membership?.LastSeenUtc,
                            reason = "failed",
                            shared_fallback = true
                        }, cancellationToken).ConfigureAwait(false);
                        await EmitHomeNodeJourneyAsync(
                            context,
                            "home_node_dispatch_fallback",
                            "response",
                            HomeNodeComponent(nodeId),
                            "shared-compute",
                            "Linked home node failed local execution; shared compute fallback will continue.",
                            BuildRouteMetadata(assignment, node, membership, "failed", workId, "failed"),
                            cancellationToken).ConfigureAwait(false);
                        return true;
                    }

                    ApplyTerminalExecutionState(
                        context,
                        "degraded",
                        "home_node",
                        "failed",
                        "Your assigned home node encountered a local execution failure.",
                        BuildFailureMessage(status.Data.Result, "Home node execution failed."));
                    await AppendLedgerEventAsync(context, "home_node.dispatch_blocked", new
                    {
                        work_id = workId,
                        node_id = nodeId,
                        membership_status = membership?.Status,
                        membership_last_seen_utc = membership?.LastSeenUtc,
                        reason = "failed",
                        result = status.Data.Result
                    }, cancellationToken).ConfigureAwait(false);
                    await EmitHomeNodeJourneyAsync(
                        context,
                        "home_node_dispatch_blocked",
                        "response",
                        HomeNodeComponent(nodeId),
                        "cwi-orchestrator",
                        "Linked home node failed local execution and shared fallback is disabled.",
                        BuildRouteMetadata(assignment, node, membership, "failed", workId, "failed"),
                        cancellationToken).ConfigureAwait(false);
                    return false;
            }

            await Task.Delay(_options.HomeNodeImmediatePollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static bool HasLocalEvidence(IReadOnlyDictionary<string, object?> result)
    {
        if (ExtractHomeNodeSources(result).Count > 0)
        {
            return true;
        }

        if (TryGetObject(result, "trace") is not { } trace)
        {
            return false;
        }

        if (TryGetBoolean(trace, "reasoning_based") == true)
        {
            return true;
        }

        return TryGetInt(trace, "source_count").GetValueOrDefault() > 0
               || TryGetInt(trace, "baseline_source_count").GetValueOrDefault() > 0;
    }

    private static void ApplyCompletedResult(
        AskWorkflowContext context,
        HomeNodeWorkReceipt receipt,
        string nodeId)
    {
        context.Answer = TryGetString(receipt.Result, "answer")
            ?? "Local execution completed.";
        context.ExecutionState = "completed";
        context.ExecutionTarget = "home_node";
        context.DeferredWorkId = receipt.WorkId;
        context.HomeNodeStatus = "completed";
        context.ExecutionMessage = "Local execution completed.";
        context.ModelName = "home-node";

        context.HomeNodeSources = ExtractHomeNodeSources(receipt.Result);
        if (TryGetObject(receipt.Result, "trace") is { } trace)
        {
            context.HomeNodeExecutionMode = TryGetString(trace, "execution_mode");
            context.HomeNodeLocalPolicyOutcome = TryGetString(trace, "local_policy_outcome");
            context.HomeNodeBundleId = TryGetString(trace, "bundle_id");
            context.HomeNodePackId = TryGetString(trace, "pack_id");
            context.HomeNodePackName = TryGetString(trace, "pack_name");
            context.HomeNodePackAnchor = TryGetString(trace, "pack_anchor");
            context.HomeNodeSearchTerms = TryGetStringList(trace, "search_terms");
        }
    }

    private static IReadOnlyList<HomeNodeSourceDto> ExtractHomeNodeSources(
        IReadOnlyDictionary<string, object?> values)
    {
        if (!values.TryGetValue("sources", out var rawSources) || rawSources is null)
        {
            return Array.Empty<HomeNodeSourceDto>();
        }

        if (rawSources is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
        {
            return arrayElement.EnumerateArray()
                .Select(MapHomeNodeSource)
                .Where(source => source is not null)
                .Cast<HomeNodeSourceDto>()
                .ToList();
        }

        if (rawSources is IEnumerable<object?> items)
        {
            var results = new List<HomeNodeSourceDto>();
            foreach (var item in items)
            {
                var source = item switch
                {
                    JsonElement element when element.ValueKind == JsonValueKind.Object => MapHomeNodeSource(element),
                    Dictionary<string, object?> mapping => MapHomeNodeSource(mapping),
                    _ => null
                };
                if (source is not null)
                {
                    results.Add(source);
                }
            }

            return results;
        }

        return Array.Empty<HomeNodeSourceDto>();
    }

    private static HomeNodeSourceDto? MapHomeNodeSource(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new HomeNodeSourceDto
        {
            SourceKind = GetJsonString(element, "source_kind") ?? string.Empty,
            Title = GetJsonString(element, "title") ?? string.Empty,
            Path = GetJsonString(element, "path") ?? string.Empty,
            Score = GetJsonInt(element, "score"),
            Excerpt = GetJsonString(element, "excerpt"),
            SizeBytes = GetJsonInt(element, "size_bytes")
        };
    }

    private static HomeNodeSourceDto? MapHomeNodeSource(IReadOnlyDictionary<string, object?> values)
    {
        var path = TryGetString(values, "path");
        var title = TryGetString(values, "title");
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new HomeNodeSourceDto
        {
            SourceKind = TryGetString(values, "source_kind") ?? string.Empty,
            Title = title ?? string.Empty,
            Path = path ?? string.Empty,
            Score = TryGetInt(values, "score"),
            Excerpt = TryGetString(values, "excerpt"),
            SizeBytes = TryGetInt(values, "size_bytes")
        };
    }

    private static string BuildFailureMessage(
        IReadOnlyDictionary<string, object?> details,
        string fallback)
    {
        if (TryGetString(details, "message") is { Length: > 0 } explicitMessage)
        {
            return explicitMessage;
        }

        if (TryGetString(details, "ack_status") is { Length: > 0 } ackStatus)
        {
            return $"Home node rejected the request: {ackStatus}.";
        }

        return fallback;
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => value.ToString()
        };
    }

    private static int? TryGetInt(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            int direct => direct,
            long directLong when directLong >= int.MinValue && directLong <= int.MaxValue => (int)directLong,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) => number,
            JsonElement element when element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed) => parsed,
            string text when int.TryParse(text, out var parsedText) => parsedText,
            _ => null
        };
    }

    private static double? TryGetDouble(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            double direct => direct,
            float directFloat => directFloat,
            decimal directDecimal => (double)directDecimal,
            int directInt => directInt,
            long directLong => directLong,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) => number,
            JsonElement element when element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out var parsed) => parsed,
            string text when double.TryParse(text, out var parsedText) => parsedText,
            _ => null
        };
    }

    private static Dictionary<string, object?>? TryGetObject(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            Dictionary<string, object?> mapping => mapping,
            JsonElement element when element.ValueKind == JsonValueKind.Object =>
                JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), JsonOptions),
            _ => null
        };
    }

    private static IReadOnlyList<string> TryGetStringList(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } arrayElement)
        {
            return arrayElement.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        if (value is IEnumerable<object?> items)
        {
            return items.OfType<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        return Array.Empty<string>();
    }

    private static string? GetJsonString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
            _ => null
        };
    }

    private static int? GetJsonInt(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record HomeNodeModelRoute(
        bool UseHomeNode,
        string Mode,
        string Reason,
        int? MaxContextTokens,
        double? GpuVramGb,
        Dictionary<string, object?> ModelProfile)
    {
        public Dictionary<string, object?> ToMetadata()
        {
            return new Dictionary<string, object?>
            {
                ["use_home_node"] = UseHomeNode,
                ["mode"] = Mode,
                ["reason"] = Reason,
                ["max_context_tokens"] = MaxContextTokens,
                ["gpu_vram_gb"] = GpuVramGb,
                ["model_profile"] = ModelProfile,
                ["pack_guidance_mode"] = "compact_cached",
                ["retrieval_mode"] = "relevant_chunks_only",
                ["heavy_reasoning_target"] = "rack_core"
            };
        }
    }

    private readonly record struct NodeAvailability(bool Dispatchable, string Status);
}
#nullable disable
