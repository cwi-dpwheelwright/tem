using System.Net;
using Cwi.Common;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Mcp;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class ActionMcpWorkflowStepTests
{
    [Fact]
    public async Task RegistryPolicyStep_blocks_unregistered_tool_when_policy_requires_registry()
    {
        var step = new ActionMcpRegistryPolicyStep(
            new McpExecutionOptions { Enabled = true },
            new FakeRegistryClient(new McpToolRegistryDocument()),
            new FakePolicyClient(new McpPolicyDocument
            {
                Rules = new[]
                {
                    new McpPolicyRule
                    {
                        Id = "block-unauthorized-tools",
                        Enforce = true
                    }
                }
            }),
            new McpPolicyEvaluator());

        var context = new ActionWorkflowContext("tenant", "user", "observe", "unknown.tool", new Dictionary<string, object?>());

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        context.McpRegistryChecked.Should().BeTrue();
        context.McpPolicyBlocked.Should().BeTrue();
        context.McpPolicy!.Reason.Should().Contain("not registered");
    }

    [Fact]
    public async Task DecisionStep_creates_approval_ticket_when_mcp_policy_requires_approval()
    {
        var tickets = new InMemoryApprovalTicketRepository();
        var step = new ActionDecisionStep(new PolicyEvaluator(), tickets);
        var context = new ActionWorkflowContext("tenant", "user", "observe", "email.send", new Dictionary<string, object?>())
        {
            McpRequiresApproval = true,
            McpPolicy = new McpPolicyEvaluation(false, true, true, "Tool is explicitly sensitive."),
            ModelName = "model",
            PolicyVersion = "v1"
        };

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        context.Executed.Should().BeFalse();
        context.ApprovalTicket.Should().NotBeNull();
        context.ApprovalTicket!.Reason.Should().Be("Tool is explicitly sensitive.");
    }

    [Fact]
    public async Task ExecutionStep_records_ledger_event_for_governed_mcp_execution()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = new ActionMcpExecutionStep(
            new McpExecutionOptions { Enabled = true },
            new FakeExecutor(new McpExecutionResult(true, new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["tool"] = "system.status"
            })),
            new McpActionLedgerRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider())));
        var context = new ActionWorkflowContext("tenant", "user", "observe", "system.status", new Dictionary<string, object?>())
        {
            TraceId = "trace-mcp",
            Executed = true,
            McpRequiresLedger = true,
            McpPolicy = new McpPolicyEvaluation(false, false, true, "Tool allowed by MCP policy."),
            McpTool = new McpToolDefinition
            {
                Name = "system.status",
                Category = "cwi_operations",
                Method = "GET",
                Endpoint = "internal://system/status",
                LogToLedger = true
            }
        };

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        context.Executed.Should().BeTrue();
        ledger.Events.Should().ContainSingle();
        ledger.Events[0].EventType.Should().Be("mcp.action");
        ledger.Events[0].PayloadJson.Should().Contain("system.status");
    }

    [Fact]
    public void PolicyEvaluator_blocks_tool_that_refuses_required_ledger_logging()
    {
        var evaluator = new McpPolicyEvaluator();
        var policy = new McpPolicyDocument
        {
            Rules = new[]
            {
                new McpPolicyRule
                {
                    Id = "require-ledger-logging",
                    Enforce = true
                }
            }
        };

        var result = evaluator.Evaluate(
            "unsafe.noledger",
            new McpToolDefinition
            {
                Name = "unsafe.noledger",
                Category = "automation",
                Endpoint = "internal://unsafe",
                LogToLedger = false
            },
            policy);

        result.Blocked.Should().BeTrue();
        result.RequiresLedger.Should().BeTrue();
        result.Reason.Should().Contain("ledger logging is required");
    }

    [Fact]
    public void PolicyEvaluator_blocks_unapproved_tool_in_read_only_mode()
    {
        var evaluator = new McpPolicyEvaluator();

        var result = evaluator.Evaluate(
            "email.send",
            new McpToolDefinition
            {
                Name = "email.send",
                Category = "personal_assistant",
                Method = "POST",
                Endpoint = "connector://email/send",
                RequiresApproval = true,
                LogToLedger = true
            },
            new McpPolicyDocument
            {
                Rules = new[]
                {
                    new McpPolicyRule { Id = "require-ledger-logging", Enforce = true }
                }
            },
            new McpExecutionOptions
            {
                Enabled = true,
                ExecutionMode = "read_only",
                ReadOnlyAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system.status"
                }
            },
            intentConfidence: 0.92d);

        result.Blocked.Should().BeTrue();
        result.RequiresLedger.Should().BeTrue();
        result.Reason.Should().Contain("not approved for Day-1 read-only execution");
    }

    [Fact]
    public void PolicyEvaluator_blocks_tool_execution_below_confidence_threshold()
    {
        var evaluator = new McpPolicyEvaluator();

        var result = evaluator.Evaluate(
            "system.status",
            new McpToolDefinition
            {
                Name = "system.status",
                Category = "cwi_operations",
                Method = "GET",
                Endpoint = "internal://system/status",
                LogToLedger = true
            },
            new McpPolicyDocument(),
            new McpExecutionOptions { Enabled = true, ExecutionMode = "read_only" },
            intentConfidence: 0.62d);

        result.Blocked.Should().BeTrue();
        result.RequiresLedger.Should().BeTrue();
        result.Reason.Should().Contain("clarification");
    }

    [Fact]
    public void PolicyEvaluator_blocks_all_tools_when_runtime_mode_is_disabled()
    {
        var evaluator = new McpPolicyEvaluator();

        var result = evaluator.Evaluate(
            "system.status",
            new McpToolDefinition
            {
                Name = "system.status",
                Category = "cwi_operations",
                Method = "GET",
                Endpoint = "internal://system/status",
                LogToLedger = true
            },
            new McpPolicyDocument(),
            new McpExecutionOptions { Enabled = true, ExecutionMode = "disabled" },
            intentConfidence: 0.95d);

        result.Blocked.Should().BeTrue();
        result.RequiresLedger.Should().BeTrue();
        result.Reason.Should().Contain("disabled");
    }

    [Fact]
    public async Task Executor_returns_connector_disabled_when_connector_mapping_is_disabled()
    {
        var configPath = WriteConnectorConfig("calendar-primary", enabled: false);
        try
        {
            var executor = CreateExecutor(new McpExecutionOptions
            {
                ConnectorsConfigPath = configPath
            });

            var response = await executor.ExecuteAsync(
                new McpToolDefinition
                {
                    Name = "calendar.view",
                    Endpoint = "connector://calendar/view",
                    ConnectorId = "calendar-primary",
                    Method = "GET"
                },
                "tenant",
                "user",
                "trace",
                new Dictionary<string, object?>(),
                CancellationToken.None);

            response.IsSuccess.Should().BeTrue();
            response.Data!.Executed.Should().BeFalse();
            response.Data.Result["status"].Should().Be("connector_disabled");
            response.Data.Result["connector_id"].Should().Be("calendar-primary");
            response.Data.Result["connector_type"].Should().Be("outlook");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Executor_keeps_enabled_connector_adapter_gated_until_provider_activation()
    {
        var configPath = WriteConnectorConfig("calendar-primary", enabled: true);
        try
        {
            var executor = CreateExecutor(new McpExecutionOptions
            {
                ConnectorsConfigPath = configPath
            });

            var response = await executor.ExecuteAsync(
                new McpToolDefinition
                {
                    Name = "calendar.view",
                    Endpoint = "connector://calendar/view",
                    ConnectorId = "calendar-primary",
                    Method = "GET"
                },
                "tenant",
                "user",
                "trace",
                new Dictionary<string, object?>(),
                CancellationToken.None);

            response.IsSuccess.Should().BeTrue();
            response.Data!.Executed.Should().BeFalse();
            response.Data.Result["status"].Should().Be("connector_adapter_not_wired");
            response.Data.Result["message"].Should().Be("Connector 'calendar-primary' is enabled, but provider execution remains gated until approval.");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Executor_returns_provider_failure_for_http_tool_error()
    {
        var executor = CreateExecutor(
            new McpExecutionOptions(),
            new HttpClient(new StaticHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{\"error\":\"down\"}")
            })));

        var response = await executor.ExecuteAsync(
            new McpToolDefinition
            {
                Name = "weather.get",
                Endpoint = "https://example.invalid/weather",
                Method = "GET"
            },
            "tenant",
            "user",
            "trace",
            new Dictionary<string, object?>(),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(502);
        response.Message.Should().Be("MCP tool execution failed");
    }

    [Fact]
    public async Task Executor_falls_back_to_html_search_results_when_instant_answer_has_no_results()
    {
        var executor = CreateExecutor(
            new McpExecutionOptions(),
            new HttpClient(new SequenceHttpMessageHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                },
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
<html>
  <body>
    <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fai-news">AI News Headline</a>
    <a class="result__snippet">A current AI development was reported.</a>
  </body>
</html>
""")
                })));

        var response = await executor.ExecuteAsync(
            new McpToolDefinition
            {
                Name = "search.web",
                Endpoint = "external://web-search",
                Method = "GET"
            },
            "tenant",
            "user",
            "trace",
            new Dictionary<string, object?> { ["query"] = "latest AI news" },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Executed.Should().BeTrue();
        response.Data.Result["provider"].Should().Be("duckduckgo_html");
        var results = response.Data.Result["results"].Should().BeAssignableTo<Dictionary<string, object?>[]>().Subject;
        results.Should().ContainSingle();
        results[0]["title"].Should().Be("AI News Headline");
        results[0]["snippet"].Should().Be("A current AI development was reported.");
        results[0]["url"].Should().Be("https://example.com/ai-news");
    }

    [Fact]
    public async Task ExecutionStep_records_ledger_event_for_failed_adapter_result()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = new ActionMcpExecutionStep(
            new McpExecutionOptions { Enabled = true },
            new FakeExecutor(new McpExecutionResult(false, new Dictionary<string, object?>
            {
                ["status"] = "connector_disabled",
                ["tool"] = "calendar.view"
            })),
            new McpActionLedgerRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider())));
        var context = new ActionWorkflowContext("tenant", "user", "observe", "calendar.view", new Dictionary<string, object?>())
        {
            TraceId = "trace-disabled-connector",
            Executed = true,
            McpRequiresLedger = true,
            McpPolicy = new McpPolicyEvaluation(false, false, true, "Tool allowed by MCP policy."),
            McpTool = new McpToolDefinition
            {
                Name = "calendar.view",
                Category = "personal_assistant",
                Method = "GET",
                Endpoint = "connector://calendar/view",
                ConnectorId = "calendar-primary",
                LogToLedger = true
            }
        };

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        context.Executed.Should().BeFalse();
        context.Result!["status"].Should().Be("connector_disabled");
        ledger.Events.Should().ContainSingle();
        ledger.Events[0].PayloadJson.Should().Contain("calendar.view");
        ledger.Events[0].PayloadJson.Should().Contain("connector_disabled");
    }

    private sealed class FakeRegistryClient : IMcpRegistryClient
    {
        private readonly McpToolRegistryDocument _document;

        public FakeRegistryClient(McpToolRegistryDocument document)
        {
            _document = document;
        }

        public Task<ServiceResponse<McpToolRegistryDocument>> GetRegistryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<McpToolRegistryDocument>.Success(_document));
        }
    }

    private static GovernedMcpToolExecutor CreateExecutor(
        McpExecutionOptions options,
        HttpClient? client = null)
    {
        return new GovernedMcpToolExecutor(
            new FakeHttpClientFactory(client ?? new HttpClient(new StaticHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            }))),
            new InMemoryLedgerRepository(),
            new FakeHomeNodeRepository(),
            CreateApplicationOptions(),
            new McpConnectorCatalog(options));
    }

    private static string WriteConnectorConfig(string connectorId, bool enabled)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, $$"""
tenant_id: "tenant"
workspace_id: "workspace"
connectors:
  - id: "{{connectorId}}"
    type: "outlook"
    enabled: {{enabled.ToString().ToLowerInvariant()}}
    config:
      mailbox: "me"
    auth:
      type: "api_key"
      api_key:
        header_name: "Authorization"
        value: "env://TOKEN"
""");
        return path;
    }

    private static ApplicationOptions CreateApplicationOptions()
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
            ledgerHashSuiteId: "sha256-v1");
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StaticHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class SequenceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FakePolicyClient : IMcpPolicyClient
    {
        private readonly McpPolicyDocument _document;

        public FakePolicyClient(McpPolicyDocument document)
        {
            _document = document;
        }

        public Task<ServiceResponse<McpPolicyDocument>> GetPolicyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<McpPolicyDocument>.Success(_document));
        }
    }

    private sealed class FakeExecutor : IMcpToolExecutor
    {
        private readonly McpExecutionResult _result;

        public FakeExecutor(McpExecutionResult result)
        {
            _result = result;
        }

        public Task<ServiceResponse<McpExecutionResult>> ExecuteAsync(
            McpToolDefinition tool,
            string tenantId,
            string userId,
            string traceId,
            IReadOnlyDictionary<string, object?> payload,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<McpExecutionResult>.Success(_result));
        }
    }

    private sealed class InMemoryApprovalTicketRepository : IApprovalTicketRepository
    {
        private readonly List<ApprovalTicket> _tickets = new();

        public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(ticket => ticket.TicketId == ticketId));
        }

        public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(ticket => ticket.IdempotencyKey == idempotencyKey));
        }

        public Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ApprovalTicket>>(_tickets);
        }

        public Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            _tickets.Add(ticket);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.LastOrDefault(item => item.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events
                .Where(item => item.TraceId == traceId)
                .Skip(skip)
                .Take(take)
                .ToArray());
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count(item => item.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(DateTimeOffset? startUtc, DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHomeNodeRepository : IHomeNodeRepository
    {
        public Task<HomeNodeRecord?> GetAsync(string tenantId, string nodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<HomeNodeRecord?>(null);
        }

        public Task<HomeNodeRecord?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken)
        {
            return Task.FromResult<HomeNodeRecord?>(null);
        }

        public Task<IReadOnlyList<HomeNodeRecord>> ListAsync(
            string tenantId,
            string? ownerUserId,
            string? status,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<HomeNodeRecord>>(Array.Empty<HomeNodeRecord>());
        }

        public Task UpsertAsync(HomeNodeRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
