#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Cwi.Orchestrator.UnitTests;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.HomeNodes;

public sealed class HomeNodeDispatchServiceTests
{
    [Fact]
    public async Task TryDispatchAskAsync_queues_work_for_active_home_node()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        HttpRequestMessage? recordedRequest = null;
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            recordedRequest = request;
            requestCount += 1;
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\"},\"capabilities\":{\"local_ask\":true}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"queued\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"work_id\":\"work-queued-1\",\"status\":\"completed\",\"result\":{\"answer\":\"local answer\",\"sources\":[{\"source_kind\":\"bundle\",\"title\":\"notes/cooling.md\",\"path\":\"/var/cwi/bundles/notes/cooling.md\",\"score\":9,\"excerpt\":\"Ceramic cooling protects the Atlas engine.\",\"size_bytes\":128}],\"trace\":{\"execution_mode\":\"local_artifact_search\",\"local_policy_outcome\":\"ALLOW_LOCAL_ONLY_NO_EGRESS\",\"bundle_id\":\"bundle-thermal\",\"pack_id\":\"thermal-pack\",\"pack_name\":\"Thermal Operations\",\"pack_anchor\":\"anchor-123\",\"search_terms\":[\"atlas\",\"cooling\"]}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var options = CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value");
        var service = CreateService(state, httpClient, options);
        var context = CreateContext();

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeFalse();
        context.ExecutionState.Should().Be("completed");
        context.ExecutionTarget.Should().Be("home_node");
        context.DeferredWorkId.Should().Be("work-queued-1");
        context.HomeNodeId.Should().Be("home-node-01");
        context.HomeNodeOwnerUserId.Should().Be("user-1");
        context.HomeNodeMembershipStatus.Should().Be("active");
        context.HomeNodeStatus.Should().Be("completed");
        context.ExecutionMessage.Should().Be("Local execution completed.");
        context.Answer.Should().Be("local answer");
        context.HomeNodeSources.Should().ContainSingle();
        context.HomeNodeSources[0].Title.Should().Be("notes/cooling.md");
        context.HomeNodeExecutionMode.Should().Be("local_artifact_search");
        context.HomeNodeLocalPolicyOutcome.Should().Be("ALLOW_LOCAL_ONLY_NO_EGRESS");
        context.HomeNodeBundleId.Should().Be("bundle-thermal");
        context.HomeNodePackId.Should().Be("thermal-pack");
        context.HomeNodePackName.Should().Be("Thermal Operations");
        context.HomeNodePackAnchor.Should().Be("anchor-123");
        context.HomeNodeSearchTerms.Should().BeEquivalentTo(new[] { "atlas", "cooling" });
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_requested");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_queued");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_completed");
        requestCount.Should().BeGreaterThanOrEqualTo(2);
        recordedRequest.Should().NotBeNull();
        recordedRequest!.Headers.TryGetValues("X-Admin-Token", out var tokenValues).Should().BeTrue();
        tokenValues.Should().ContainSingle().Which.Should().Be("coord-token");
    }

    [Fact]
    public async Task TryDispatchAskAsync_continues_shared_path_without_node_dispatch_when_user_has_no_linked_node()
    {
        var state = new InMemoryHomeNodeState();
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("unlinked users should not dispatch home-node work")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext();

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.ExecutionTarget.Should().Be("shared_compute");
        context.HomeNodeId.Should().BeNull();
        context.HomeNodeStatus.Should().BeNull();
        state.Ledger.Events.Should().NotContain(e => e.EventType.StartsWith("home_node.dispatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryDispatchAskAsync_keeps_controlled_fact_memory_work_on_shared_path()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("controlled fact memory work should not call coordination dispatch")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("Who owns CWI-RL-001 observability?");
        context.QueryClassification = "controlled_fact_lookup";
        context.IntentType = "memory_recall";
        context.RetrievalDomain = "controlled_fact";
        context.RetrievalAllowed = true;
        context.GroundingMode = "source_grounded";

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.HomeNodeStatus.Should().Be("skipped_for_governed_query");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_skipped");
        state.Ledger.Events.Should().NotContain(e => e.EventType == "home_node.dispatch_requested");
    }

    [Fact]
    public async Task TryDispatchAskAsync_keeps_evidence_required_domain_queries_on_shared_compute()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = false,
            RemoteFallbackAllowed = true,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("source-grounded evidence query should not call coordination dispatch")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("Using governed graph evidence, who owns dashboard telemetry?");
        context.QueryClassification = "domain_request";
        context.IntentType = "decision_support";
        context.RetrievalDomain = "graph_retrieval";
        context.RetrievalAllowed = true;
        context.GroundingMode = "source_grounded";
        context.EvidenceRequired = true;

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.HomeNodeStatus.Should().Be("skipped_for_governed_query");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_skipped");
        state.Ledger.Events.Should().NotContain(e => e.EventType == "home_node.dispatch_requested");
    }

    [Fact]
    public async Task TryDispatchAskAsync_sends_small_local_model_route_for_6gb_home_node()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        string? postedBody = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\",\"model_profile\":{\"provider\":\"ollama\",\"model\":\"local-8b:q4\",\"quantization\":\"Q4_K_M\",\"context_tokens\":8192,\"max_vram_gb\":6}},\"capabilities\":{\"local_ask\":true,\"local_model\":true,\"local_model_profile\":{\"provider\":\"ollama\",\"model\":\"local-8b:q4\",\"model_family\":\"7b/8b-instruct\",\"quantization\":\"Q4_K_M\",\"context_tokens\":8192,\"max_vram_gb\":6}}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                postedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"queued\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"work_id\":\"work-queued-1\",\"status\":\"completed\",\"result\":{\"answer\":\"local model queued\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext();

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        postedBody.Should().NotBeNull();
        using var document = JsonDocument.Parse(postedBody!);
        var execution = document.RootElement.GetProperty("payload").GetProperty("execution");
        execution.GetProperty("route_mode").GetString().Should().Be("local_model");
        execution.GetProperty("route_reason").GetString().Should().Be("small_quantized_home_node_profile");
        execution.GetProperty("use_local_model").GetBoolean().Should().BeTrue();
        execution.GetProperty("max_context_tokens").GetInt32().Should().Be(4096);
        execution.GetProperty("pack_guidance_mode").GetString().Should().Be("compact_cached");
        execution.GetProperty("retrieval_mode").GetString().Should().Be("relevant_chunks_only");
        execution.GetProperty("model_profile").GetProperty("model").GetString().Should().Be("local-8b:q4");
        execution.GetProperty("model_profile").GetProperty("quantization").GetString().Should().Be("Q4_K_M");
    }

    [Fact]
    public async Task TryDispatchAskAsync_routes_heavy_reasoning_to_shared_compute_when_fallback_allowed()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = false,
            RemoteFallbackAllowed = true,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\"},\"capabilities\":{\"local_ask\":true,\"local_model\":true,\"local_model_profile\":{\"model\":\"local-8b:q4\",\"context_tokens\":4096,\"max_vram_gb\":6}}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException("heavy reasoning route should not create home-node work");
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("Please do heavy reasoning and multi-pack synthesis for production latency.");

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.ExecutionTarget.Should().Be("shared_compute");
        context.HomeNodeStatus.Should().Be("skipped_for_model_route");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_skipped");
    }

    [Fact]
    public async Task TryDispatchAskAsync_falls_back_when_completed_home_node_has_no_local_evidence()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = false,
            RemoteFallbackAllowed = true,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\"},\"capabilities\":{\"local_ask\":true}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"queued\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"work_id\":\"work-queued-1\",\"status\":\"completed\",\"result\":{\"answer\":\"I do not have matching local evidence for \\\"What is 2 + 2?\\\".\",\"sources\":[],\"trace\":{\"execution_mode\":\"local_artifact_search\",\"source_count\":0,\"baseline_source_count\":0}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("What is 2 + 2?");

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.FallbackApplied.Should().BeTrue();
        context.ExecutionTarget.Should().Be("shared_compute");
        context.HomeNodeStatus.Should().Be("no_local_evidence");
        context.Answer.Should().BeEmpty();
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_fallback");
    }

    [Fact]
    public async Task TryDispatchAskAsync_accepts_home_node_reasoning_result_without_local_evidence()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active",
            PolicyProfileVersion = "1.0",
            LastHeartbeatUtc = DateTimeOffset.UtcNow
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = false,
            RemoteFallbackAllowed = true,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\"},\"capabilities\":{\"local_ask\":true,\"local_model\":true,\"local_model_profile\":{\"model\":\"local-8b:q4\",\"context_tokens\":4096,\"max_vram_gb\":6}}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"queued\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"work_id\":\"work-queued-1\",\"status\":\"completed\",\"result\":{\"answer\":\"4\",\"sources\":[],\"trace\":{\"execution_mode\":\"local_model_ollama\",\"source_count\":0,\"baseline_source_count\":0,\"reasoning_based\":true,\"grounded\":false}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("What is 2 + 2?");

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeFalse();
        context.ExecutionTarget.Should().Be("home_node");
        context.HomeNodeStatus.Should().Be("completed");
        context.Answer.Should().Be("4");
        context.HomeNodeExecutionMode.Should().Be("local_model_ollama");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_completed");
        state.Ledger.Events.Should().NotContain(e => e.EventType == "home_node.dispatch_fallback");
    }

    [Fact]
    public async Task TryDispatchAskAsync_falls_back_to_shared_compute_when_policy_allows()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "offline"
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = false,
            RemoteFallbackAllowed = true,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("dispatch should not be called")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions());
        var context = CreateContext();

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.FallbackApplied.Should().BeTrue();
        context.ExecutionTarget.Should().Be("shared_compute");
        context.ExecutionState.Should().Be("completed");
        context.HomeNodeStatus.Should().Be("offline");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_fallback");
    }

    [Fact]
    public async Task TryDispatchAskAsync_keeps_governed_identity_queries_on_shared_compute()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active"
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("dispatch should not be called")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions());
        var context = CreateContext();
        context.QueryClassification = "identity_definition";

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeTrue();
        context.ExecutionTarget.Should().Be("shared_compute");
        context.HomeNodeId.Should().Be("home-node-01");
        context.HomeNodeOwnerUserId.Should().Be("user-1");
        context.HomeNodeStatus.Should().Be("skipped_for_governed_query");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_skipped");
    }

    [Theory]
    [InlineData("general_reasoning")]
    [InlineData("troubleshooting")]
    public async Task TryDispatchAskAsync_dispatches_broad_chat_prompts_to_linked_home_node(
        string classification)
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "active"
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/membership", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-01\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-1\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"health\":{\"status\":\"READY\",\"model_profile\":{\"provider\":\"ollama\",\"model\":\"local-8b:q4\",\"context_tokens\":4096,\"max_vram_gb\":6}},\"capabilities\":{\"local_ask\":true,\"local_model\":true,\"local_model_profile\":{\"model\":\"local-8b:q4\",\"context_tokens\":4096,\"max_vram_gb\":6}}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Post &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/node-work", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"queued\"}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (request.Method == HttpMethod.Get &&
                string.Equals(request.RequestUri?.AbsolutePath, "/api/node-work/work-queued-1", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"work_id\":\"work-queued-1\",\"status\":\"completed\",\"result\":{\"answer\":\"local node answer\",\"sources\":[],\"trace\":{\"execution_mode\":\"local_model_ollama\",\"reasoning_based\":true}}}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected request {request.Method} {request.RequestUri?.AbsolutePath}");
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions(
            coordinationBaseUrl: "http://coordination",
            coordinationAdminToken: "coord-token",
            homeNodeWorkTokenSecret: "secret-value"));
        var context = CreateContext("why is the water in the ocean salty?");
        context.QueryClassification = classification;

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeFalse();
        context.ExecutionTarget.Should().Be("home_node");
        context.HomeNodeStatus.Should().Be("completed");
        context.Answer.Should().Be("local node answer");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_requested");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_completed");
    }

    [Fact]
    public async Task TryDispatchAskAsync_blocks_when_local_only_home_node_is_unavailable()
    {
        var state = new InMemoryHomeNodeState();
        state.Nodes["tenant-1:home-node-01"] = new HomeNodeRecord
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "offline"
        };
        state.Assignments["tenant-1:user-1"] = new HomeNodeAssignment
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            LocalOnly = true,
            RemoteFallbackAllowed = false,
            Status = "active",
            AssignedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("dispatch should not be called")))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = CreateService(state, httpClient, CreateOptions());
        var context = CreateContext();

        var result = await service.TryDispatchAskAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ContinueSharedPath.Should().BeFalse();
        context.ExecutionState.Should().Be("offline");
        context.ExecutionTarget.Should().Be("home_node");
        context.HomeNodeStatus.Should().Be("offline");
        context.Answer.Should().Contain("unavailable");
        state.Ledger.Events.Should().Contain(e => e.EventType == "home_node.dispatch_blocked");
    }

    [Fact]
    public async Task DeferredAskService_returns_completed_home_node_answer_for_visible_work()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsolutePath.Should().Be("/api/node-work/work-1");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"work_id\":\"work-1\",\"status\":\"completed\",\"result\":{\"answer\":\"final home answer\",\"trace\":{\"request_id\":\"req-1\",\"trace_id\":\"trace-1\",\"sources\":[],\"confidence\":0.77,\"policy_mode\":\"observe\",\"model\":\"home-node\",\"pack_hash\":\"pack-hash\",\"execution_state\":\"completed\",\"execution_target\":\"home_node\",\"deferred_work_id\":\"work-1\"}}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = new HomeNodeDeferredAskService(
            new HomeNodeDispatchClient(httpClient, CreateOptions(
                coordinationBaseUrl: "http://coordination",
                coordinationAdminToken: "coord-token")),
            new StubJourneyService(new[]
            {
                new JourneyEvent
                {
                    RequestId = "req-1",
                    TraceId = "trace-1",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    Metadata = new Dictionary<string, object?> { ["work_id"] = "work-1" }
                }
            }));

        var result = await service.GetAsync("tenant-1", "user-1", "req-1", "work-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.ExecutionState.Should().Be("completed");
        result.Data.ExecutionTarget.Should().Be("home_node");
        result.Data.DeferredWorkId.Should().Be("work-1");
        result.Data.Answer.Should().Be("final home answer");
        result.Data.Trace.PolicyMode.Should().Be("observe");
        result.Data.Trace.PackHash.Should().Be("pack-hash");
    }

    [Fact]
    public async Task DeferredAskService_rejects_work_without_visible_journey_link()
    {
        var calls = 0;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            calls += 1;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        var service = new HomeNodeDeferredAskService(
            new HomeNodeDispatchClient(httpClient, CreateOptions(
                coordinationBaseUrl: "http://coordination",
                coordinationAdminToken: "coord-token")),
            new StubJourneyService(new[]
            {
                new JourneyEvent
                {
                    RequestId = "req-1",
                    TraceId = "trace-1",
                    TenantId = "tenant-1",
                    UserId = "another-user",
                    Metadata = new Dictionary<string, object?> { ["work_id"] = "work-1" }
                }
            }));

        var result = await service.GetAsync("tenant-1", "user-1", "req-1", "work-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        calls.Should().Be(0);
    }

    private static HomeNodeDispatchService CreateService(
        InMemoryHomeNodeState state,
        HttpClient httpClient,
        ApplicationOptions options)
    {
        return new HomeNodeDispatchService(
            state,
            state,
            state,
            new HomeNodeDispatchClient(httpClient, options),
            new HomeNodeWorkTokenService(options),
            JourneyTestHelpers.CreateEmitter("req-1"),
            options,
            state.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            NullLogger<HomeNodeDispatchService>.Instance);
    }

    private static ApplicationOptions CreateOptions(
        string? coordinationBaseUrl = null,
        string? coordinationAdminToken = null,
        string? homeNodeWorkTokenSecret = null,
        int homeNodeImmediateWaitTimeoutSeconds = 1)
    {
        return new ApplicationOptions(
            packsBaseUrl: "http://packs",
            packsDirectory: null,
            graphEnabled: false,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://v1",
            vectorShard2Url: "http://v2",
            vectorEmbeddingDimension: 8,
            modelBaseUrl: "http://model",
            modelName: "model-default",
            modelTimeoutSeconds: 5,
            policyVersion: "v1",
            ledgerHashSuiteId: "sha256-v1",
            coordinationBaseUrl: coordinationBaseUrl,
            coordinationAdminToken: coordinationAdminToken,
            homeNodeWorkTokenSecret: homeNodeWorkTokenSecret,
            homeNodeHeartbeatMaxAgeSeconds: 300,
            homeNodeImmediateWaitTimeoutSeconds: homeNodeImmediateWaitTimeoutSeconds,
            homeNodeImmediatePollIntervalMilliseconds: 5);
    }

    private static AskWorkflowContext CreateContext(string question = "What changed?")
    {
        return new AskWorkflowContext("tenant-1", "user-1", question, "core-pack", null, "observe", 5)
        {
            PackHash = "pack-hash-1",
            PackVersion = "1.0.0",
            PolicyVersion = "v1",
            RequestId = "req-1",
            TraceId = "trace-1",
            ModelTier = "local-first"
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class StubJourneyService : IJourneyService
    {
        private readonly IReadOnlyList<JourneyEvent> _events;

        public StubJourneyService(IReadOnlyList<JourneyEvent> events)
        {
            _events = events;
        }

        public Task<IReadOnlyList<JourneyEvent>> ListByRequestIdAsync(string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<JourneyEvent>>(
                _events.Where(item => string.Equals(item.RequestId, requestId, StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }

    private sealed class InMemoryHomeNodeState :
        IHomeNodeRepository,
        IHomeNodeAssignmentRepository,
        IHomeNodePolicyScopeRepository
    {
        public Dictionary<string, HomeNodeRecord> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HomeNodeAssignment> Assignments { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HomeNodePolicyScope> Scopes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public InMemoryLedgerRepository Ledger { get; } = new();

        Task<HomeNodeRecord?> IHomeNodeRepository.GetAsync(string tenantId, string nodeId, CancellationToken cancellationToken)
        {
            Nodes.TryGetValue($"{tenantId}:{nodeId}", out var node);
            return Task.FromResult(node);
        }

        Task<HomeNodeRecord?> IHomeNodeRepository.FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken)
        {
            var node = Nodes.Values.FirstOrDefault(x => string.Equals(x.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(node);
        }

        Task<IReadOnlyList<HomeNodeRecord>> IHomeNodeRepository.ListAsync(string tenantId, string? ownerUserId, string? status, CancellationToken cancellationToken)
        {
            IReadOnlyList<HomeNodeRecord> items = Nodes.Values.ToList();
            return Task.FromResult(items);
        }

        Task IHomeNodeRepository.UpsertAsync(HomeNodeRecord record, CancellationToken cancellationToken)
        {
            Nodes[$"{record.TenantId}:{record.NodeId}"] = record;
            return Task.CompletedTask;
        }

        Task<HomeNodeAssignment?> IHomeNodeAssignmentRepository.GetAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            Assignments.TryGetValue($"{tenantId}:{userId}", out var assignment);
            return Task.FromResult(assignment);
        }

        Task<IReadOnlyList<HomeNodeAssignment>> IHomeNodeAssignmentRepository.ListAsync(string tenantId, string? userId, CancellationToken cancellationToken)
        {
            IReadOnlyList<HomeNodeAssignment> items = Assignments.Values.ToList();
            return Task.FromResult(items);
        }

        Task IHomeNodeAssignmentRepository.UpsertAsync(HomeNodeAssignment assignment, CancellationToken cancellationToken)
        {
            Assignments[$"{assignment.TenantId}:{assignment.UserId}"] = assignment;
            return Task.CompletedTask;
        }

        Task<HomeNodePolicyScope?> IHomeNodePolicyScopeRepository.GetAsync(string tenantId, string policyScopeId, CancellationToken cancellationToken)
        {
            Scopes.TryGetValue($"{tenantId}:{policyScopeId}", out var scope);
            return Task.FromResult(scope);
        }

        Task<IReadOnlyList<HomeNodePolicyScope>> IHomeNodePolicyScopeRepository.ListAsync(string tenantId, CancellationToken cancellationToken)
        {
            IReadOnlyList<HomeNodePolicyScope> items = Scopes.Values.ToList();
            return Task.FromResult(items);
        }

        Task IHomeNodePolicyScopeRepository.UpsertAsync(HomeNodePolicyScope scope, CancellationToken cancellationToken)
        {
            Scopes[$"{scope.TenantId}:{scope.PolicyScopeId}"] = scope;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault(x => x.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            IReadOnlyList<LedgerEvent> items = Events.Where(x => x.TraceId == traceId).Skip(skip).Take(take).ToList();
            return Task.FromResult(items);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count(x => x.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(DateTimeOffset? startUtc, DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
        {
            IReadOnlyList<LedgerEvent> items = Events
                .Where(x => string.IsNullOrWhiteSpace(eventType) || x.EventType == eventType)
                .ToList();
            return Task.FromResult(items);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
