#nullable enable
using System.Net;
using System.Text;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.HomeNodes;

public sealed class HomeNodeServicesTests
{
    [Fact]
    public async Task RegistryService_registers_and_updates_home_node()
    {
        var repos = new InMemoryHomeNodeState();
        var service = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var create = await service.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            DeviceClass = "personal-core",
            TailscaleIp = "100.120.10.24",
            Capabilities = new Dictionary<string, object?>
            {
                ["local_retrieval"] = true
            },
            PackVersions = new Dictionary<string, object?>
            {
                ["core-pack"] = "2026.04.04"
            }
        }, CancellationToken.None);

        create.IsSuccess.Should().BeTrue();
        create.Data!.Capabilities.Should().ContainKey("local_retrieval");
        repos.Ledger.Events.Should().ContainSingle(e => e.EventType == "home_node.registered");

        var update = await service.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            Status = "healthy"
        }, CancellationToken.None);

        update.IsSuccess.Should().BeTrue();
        update.Data!.Status.Should().Be("healthy");
        repos.Ledger.Events.Should().Contain(e => e.EventType == "home_node.updated");

        var list = await service.ListAsync("tenant-1", "user-1", null, CancellationToken.None);
        list.IsSuccess.Should().BeTrue();
        list.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task PolicyScopeService_registers_scope_and_returns_it()
    {
        var repos = new InMemoryHomeNodeState();
        var service = new HomeNodePolicyScopeService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var upsert = await service.UpsertAsync(new HomeNodePolicyScopeUpsertRequestDto
        {
            TenantId = "tenant-1",
            PolicyScopeId = "private_local_only",
            Version = "1.0",
            Rules = new Dictionary<string, object?>
            {
                ["allow_remote_fallback"] = false
            }
        }, CancellationToken.None);

        upsert.IsSuccess.Should().BeTrue();
        upsert.Data!.Rules.Should().ContainKey("allow_remote_fallback");
        repos.Ledger.Events.Should().Contain(e => e.EventType == "home_node.policy_scope_upserted");

        var get = await service.GetAsync("tenant-1", "private_local_only", CancellationToken.None);
        get.IsSuccess.Should().BeTrue();
        get.Data!.Version.Should().Be("1.0");
    }

    [Fact]
    public async Task AssignmentService_requires_registered_node_and_policy_scope()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var policyScopeService = new HomeNodePolicyScopeService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var missingNode = await assignmentService.UpsertAsync(new HomeNodeAssignmentUpsertRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "missing-node"
        }, CancellationToken.None);

        missingNode.IsSuccess.Should().BeFalse();
        missingNode.StatusCode.Should().Be(404);

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1"
        }, CancellationToken.None);

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "watcher-node-01",
            OwnerUserId = "user-1"
        }, CancellationToken.None);

        await policyScopeService.UpsertAsync(new HomeNodePolicyScopeUpsertRequestDto
        {
            TenantId = "tenant-1",
            PolicyScopeId = "private_local_only"
        }, CancellationToken.None);

        var assigned = await assignmentService.UpsertAsync(new HomeNodeAssignmentUpsertRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            PrimaryHomeNodeId = "home-node-01",
            WatcherNodeId = "watcher-node-01",
            PolicyScopeId = "private_local_only",
            LocalOnly = true,
            RemoteFallbackAllowed = false
        }, CancellationToken.None);

        assigned.IsSuccess.Should().BeTrue();
        assigned.Data!.PrimaryHomeNodeId.Should().Be("home-node-01");
        assigned.Data.PolicyScopeId.Should().Be("private_local_only");
        repos.Ledger.Events.Should().Contain(e => e.EventType == "home_node.assignment_upserted");

        var get = await assignmentService.GetAsync("tenant-1", "user-1", CancellationToken.None);
        get.IsSuccess.Should().BeTrue();
        get.Data!.WatcherNodeId.Should().Be("watcher-node-01");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_links_owned_node_from_canonical_qr_payload()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            TailscaleIp = "100.122.244.48",
            WatcherNodeId = "watcher-node-01"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "tenant_id": "tenant-1",
                  "node_id": "home-node-01",
                  "identifier_version": "machine-id-sha256-v1"
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();
        linked.Data.MatchedVia.Should().Be("node_id");
        linked.Data.Node!.NodeId.Should().Be("home-node-01");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_links_owned_node_from_wrapped_qr_payload()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            TailscaleIp = "100.122.244.48"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "tenant_id": "tenant-1",
                  "payload": {
                    "node_id": "home-node-01",
                    "identifier_version": "machine-id-sha256-v1"
                  }
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();
        linked.Data.Node!.NodeId.Should().Be("home-node-01");
        linked.Data.MatchedVia.Should().Be("node_id");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_rejects_structured_payload_without_identifiers()
    {
        var repos = new InMemoryHomeNodeState();
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "tenant_id": "tenant-1",
                  "identifier_version": "machine-id-sha256-v1"
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeFalse();
        linked.StatusCode.Should().Be(400);
        linked.Error!.Message.Should().Contain("missing node_id or tailscale_ip");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_resolves_raw_tailscale_ip()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-02",
            OwnerUserId = "user-1",
            TailscaleIp = "100.122.244.48"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = "100.122.244.48"
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.MatchedVia.Should().Be("tailscale_ip");
        linked.Data.Node!.NodeId.Should().Be("home-node-02");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_allows_multiple_users_in_same_tenant_to_link_same_node()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-03",
            OwnerUserId = "user-2",
            TailscaleIp = "100.122.244.48"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                NodeId = "home-node-03"
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();
        linked.Data.Node!.OwnerUserId.Should().Be("user-2");
        linked.Data.Assignment!.UserId.Should().Be("user-1");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_rejects_node_claimed_by_another_tenant()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-2",
            NodeId = "home-node-03",
            OwnerUserId = "user-2",
            TailscaleIp = "100.122.244.48"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                NodeId = "home-node-03"
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeFalse();
        linked.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_claims_enrolled_unclaimed_node_from_qr_payload()
    {
        var repos = new InMemoryHomeNodeState();
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(
            repos,
            repos,
            assignmentService,
            CreateDispatchClient(new StubHttpMessageHandler(request =>
            {
                request.RequestUri!.AbsolutePath.Should().Be("/api/membership");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-unclaimed\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"\",\"tailscale_ip\":\"100.122.244.48\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"capabilities\":{\"local_ask\":true},\"pack_versions\":{},\"health\":{\"status\":\"READY\"}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            })));

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "tenant_id": "tenant-1",
                  "node_id": "home-node-unclaimed",
                  "identifier_version": "machine-id-sha256-v1"
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();
        linked.Data.Node!.OwnerUserId.Should().Be("user-1");
        linked.Data.Node.TailscaleIp.Should().Be("100.122.244.48");
        linked.Data.Assignment!.PrimaryHomeNodeId.Should().Be("home-node-unclaimed");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_preserves_membership_owner_when_linking_enrolled_node()
    {
        var repos = new InMemoryHomeNodeState();
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(
            repos,
            repos,
            assignmentService,
            CreateDispatchClient(new StubHttpMessageHandler(request =>
            {
                request.RequestUri!.AbsolutePath.Should().Be("/api/membership");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-owned\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-2\",\"tailscale_ip\":\"100.122.244.48\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"capabilities\":{\"local_ask\":true},\"pack_versions\":{},\"health\":{\"status\":\"READY\"}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            })));

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "node_id": "home-node-owned",
                  "identifier_version": "machine-id-sha256-v1"
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();
        linked.Data.Node!.OwnerUserId.Should().Be("user-2");
        linked.Data.Assignment!.UserId.Should().Be("user-1");
        linked.Data.Assignment.PrimaryHomeNodeId.Should().Be("home-node-owned");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_refreshes_existing_node_from_membership_before_linking()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(
            repos,
            repos,
            assignmentService,
            CreateDispatchClient(new StubHttpMessageHandler(request =>
            {
                request.RequestUri!.AbsolutePath.Should().Be("/api/membership");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "[{\"node_id\":\"home-node-stale\",\"tenant_id\":\"tenant-1\",\"owner_user_id\":\"user-2\",\"tailscale_ip\":\"100.200.10.10\",\"watcher_node_id\":\"watcher-fresh\",\"status\":\"active\",\"last_seen_utc\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"capabilities\":{\"local_ask\":true},\"pack_versions\":{},\"health\":{\"status\":\"READY\"}}]",
                        Encoding.UTF8,
                        "application/json")
                };
            })));

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-stale",
            OwnerUserId = "deleted-smoke-user",
            TailscaleIp = "100.122.244.48",
            WatcherNodeId = "watcher-stale"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                QrCodePayload = """
                {
                  "node_id": "home-node-stale",
                  "identifier_version": "machine-id-sha256-v1"
                }
                """
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Node!.OwnerUserId.Should().Be("user-2");
        linked.Data.Node.TailscaleIp.Should().Be("100.200.10.10");
        linked.Data.Node.WatcherNodeId.Should().Be("watcher-fresh");
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_unlinks_active_assignment_and_hides_revoked_assignment()
    {
        var repos = new InMemoryHomeNodeState();
        var registryService = new HomeNodeRegistryService(
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        await registryService.UpsertAsync(new HomeNodeUpsertRequestDto
        {
            TenantId = "tenant-1",
            NodeId = "home-node-01",
            OwnerUserId = "user-1",
            TailscaleIp = "100.122.244.48"
        }, CancellationToken.None);

        var linked = await linkService.LinkAsync(
            "tenant-1",
            "user-1",
            new CurrentUserHomeNodeLinkRequestDto
            {
                NodeId = "home-node-01"
            },
            CancellationToken.None);

        linked.IsSuccess.Should().BeTrue();
        linked.Data!.Linked.Should().BeTrue();

        var unlinked = await linkService.UnlinkAsync("tenant-1", "user-1", CancellationToken.None);

        unlinked.IsSuccess.Should().BeTrue();
        unlinked.Data!.Linked.Should().BeFalse();
        unlinked.Data.Assignment.Should().BeNull();
        unlinked.Data.Node.Should().BeNull();

        var storedAssignment = await assignmentService.GetAsync("tenant-1", "user-1", CancellationToken.None);
        storedAssignment.IsSuccess.Should().BeTrue();
        storedAssignment.Data!.Status.Should().Be("revoked");
        storedAssignment.Data.RevokedUtc.Should().NotBeNull();

        var visibleAssignment = await linkService.GetAsync("tenant-1", "user-1", CancellationToken.None);
        visibleAssignment.IsSuccess.Should().BeTrue();
        visibleAssignment.Data!.Linked.Should().BeFalse();
        visibleAssignment.Data.Assignment.Should().BeNull();
        visibleAssignment.Data.Node.Should().BeNull();
    }

    [Fact]
    public async Task CurrentUserHomeNodeLinkService_unlink_is_idempotent_when_no_active_assignment()
    {
        var repos = new InMemoryHomeNodeState();
        var assignmentService = new HomeNodeAssignmentService(
            repos,
            repos,
            repos,
            repos.Ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));
        var linkService = new CurrentUserHomeNodeLinkService(repos, repos, assignmentService, CreateDispatchClient());

        var response = await linkService.UnlinkAsync("tenant-1", "user-1", CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Linked.Should().BeFalse();
        response.Data.Assignment.Should().BeNull();
        response.Data.Node.Should().BeNull();
    }

    private static HomeNodeDispatchClient CreateDispatchClient(HttpMessageHandler? handler = null)
    {
        var httpClient = new HttpClient(handler ?? new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/membership");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }))
        {
            BaseAddress = new Uri("http://coordination")
        };

        return new HomeNodeDispatchClient(
            httpClient,
            new ApplicationOptions(
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
                coordinationBaseUrl: "http://coordination"));
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

    private sealed class InMemoryHomeNodeState :
        IHomeNodeRepository,
        IHomeNodeAssignmentRepository,
        IHomeNodePolicyScopeRepository
    {
        private readonly Dictionary<string, HomeNodeRecord> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HomeNodeAssignment> _assignments = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HomeNodePolicyScope> _scopes = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryLedgerRepository Ledger { get; } = new();

        Task<HomeNodeRecord?> IHomeNodeRepository.GetAsync(string tenantId, string nodeId, CancellationToken cancellationToken)
        {
            _nodes.TryGetValue(NodeKey(tenantId, nodeId), out var node);
            return Task.FromResult(node);
        }

        Task<HomeNodeRecord?> IHomeNodeRepository.FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken)
        {
            var node = _nodes.Values.FirstOrDefault(x => string.Equals(x.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(node);
        }

        Task<IReadOnlyList<HomeNodeRecord>> IHomeNodeRepository.ListAsync(
            string tenantId,
            string? ownerUserId,
            string? status,
            CancellationToken cancellationToken)
        {
            var items = _nodes.Values
                .Where(x => x.TenantId == tenantId)
                .Where(x => string.IsNullOrWhiteSpace(ownerUserId) || x.OwnerUserId == ownerUserId)
                .Where(x => string.IsNullOrWhiteSpace(status) || x.Status == status)
                .OrderBy(x => x.NodeId)
                .ToList();

            return Task.FromResult<IReadOnlyList<HomeNodeRecord>>(items);
        }

        Task IHomeNodeRepository.UpsertAsync(HomeNodeRecord record, CancellationToken cancellationToken)
        {
            _nodes[NodeKey(record.TenantId, record.NodeId)] = record;
            return Task.CompletedTask;
        }

        Task<HomeNodeAssignment?> IHomeNodeAssignmentRepository.GetAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            _assignments.TryGetValue(AssignmentKey(tenantId, userId), out var assignment);
            return Task.FromResult(assignment);
        }

        Task<IReadOnlyList<HomeNodeAssignment>> IHomeNodeAssignmentRepository.ListAsync(
            string tenantId,
            string? userId,
            CancellationToken cancellationToken)
        {
            var items = _assignments.Values
                .Where(x => x.TenantId == tenantId)
                .Where(x => string.IsNullOrWhiteSpace(userId) || x.UserId == userId)
                .OrderBy(x => x.UserId)
                .ToList();

            return Task.FromResult<IReadOnlyList<HomeNodeAssignment>>(items);
        }

        Task IHomeNodeAssignmentRepository.UpsertAsync(HomeNodeAssignment assignment, CancellationToken cancellationToken)
        {
            _assignments[AssignmentKey(assignment.TenantId, assignment.UserId)] = assignment;
            return Task.CompletedTask;
        }

        Task<HomeNodePolicyScope?> IHomeNodePolicyScopeRepository.GetAsync(
            string tenantId,
            string policyScopeId,
            CancellationToken cancellationToken)
        {
            _scopes.TryGetValue(ScopeKey(tenantId, policyScopeId), out var scope);
            return Task.FromResult(scope);
        }

        Task<IReadOnlyList<HomeNodePolicyScope>> IHomeNodePolicyScopeRepository.ListAsync(
            string tenantId,
            CancellationToken cancellationToken)
        {
            var items = _scopes.Values
                .Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.PolicyScopeId)
                .ToList();

            return Task.FromResult<IReadOnlyList<HomeNodePolicyScope>>(items);
        }

        Task IHomeNodePolicyScopeRepository.UpsertAsync(HomeNodePolicyScope scope, CancellationToken cancellationToken)
        {
            _scopes[ScopeKey(scope.TenantId, scope.PolicyScopeId)] = scope;
            return Task.CompletedTask;
        }

        private static string NodeKey(string tenantId, string nodeId) => $"{tenantId}:{nodeId}";
        private static string AssignmentKey(string tenantId, string userId) => $"{tenantId}:{userId}";
        private static string ScopeKey(string tenantId, string policyScopeId) => $"{tenantId}:{policyScopeId}";
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
            var events = Events.Where(x => x.TraceId == traceId).Skip(skip).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(events);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count(x => x.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            var events = Events
                .Where(x => string.IsNullOrWhiteSpace(eventType) || x.EventType == eventType)
                .ToList();

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
