using Cwi.Common;
using Cwi.Orchestrator.Application.Mcp;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskMcpToolExecutionStepTests
{
    [Fact]
    public async Task Executes_allowed_read_only_tool_and_records_ledger()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "system.status",
                Category = "cwi_operations",
                Method = "GET",
                Endpoint = "internal://system/status",
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["tool"] = "system.status",
                ["message"] = "CWI orchestrator is available."
            }),
            ledger);
        var context = CreateContext("is cwi healthy", "system.status");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeTrue();
        context.McpExecutionStatus.Should().Be("executed");
        context.Route.Should().Be("mcp_tool_execution");
        context.Answer.Should().Contain("Source: System status check");
        context.Answer.Should().NotContain("Trace:");
        ledger.Events.Should().ContainSingle();
        ledger.Events[0].PayloadJson.Should().Contain("system.status");
    }

    [Fact]
    public async Task Blocks_unapproved_tool_in_read_only_mode_and_records_ledger()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "email.send",
                Category = "personal_assistant",
                Method = "POST",
                Endpoint = "connector://email/send",
                RequiresApproval = true,
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>()),
            ledger);
        var context = CreateContext("email chad and tell him we are good", "email.send");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeFalse();
        context.McpPolicyDecision.Should().Be("blocked");
        context.McpExecutionStatus.Should().Be("blocked");
        context.Answer.Should().Contain("cannot execute");
        ledger.Events.Should().ContainSingle();
        ledger.Events[0].PayloadJson.Should().Contain("not approved for Day-1 read-only execution");
    }

    [Fact]
    public async Task Routes_news_intent_to_search_web_fallback_in_day1_mode()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "search.web",
                Category = "knowledge",
                Method = "GET",
                Endpoint = "external://web-search",
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["tool"] = "search.web",
                ["provider"] = "duckduckgo_instant_answer"
            }),
            ledger);
        var context = CreateContext("wats tha newz today", "news.get");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeTrue();
        context.McpExecutionToolName.Should().Be("search.web");
        context.Answer.Should().Contain("current web search results");
        ledger.Events.Should().ContainSingle();
        ledger.Events[0].PayloadJson.Should().Contain("search.web");
    }

    [Fact]
    public async Task Renders_search_web_titles_snippets_and_sources()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "search.web",
                Category = "knowledge",
                Method = "GET",
                Endpoint = "external://web-search",
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["tool"] = "search.web",
                ["provider"] = "duckduckgo_instant_answer",
                ["results"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["title"] = "AI News Headline",
                        ["snippet"] = "A current AI development was reported.",
                        ["url"] = "https://example.com/ai-news",
                        ["source"] = "Example News"
                    }
                },
                ["sources"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["title"] = "AI News Headline",
                        ["url"] = "https://example.com/ai-news",
                        ["source"] = "Example News"
                    }
                }
            }),
            ledger);
        var context = CreateContext("look up latest AI news", "search.web");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeTrue();
        context.Answer.Should().Contain("AI News Headline");
        context.Answer.Should().Contain("A current AI development was reported.");
        context.Answer.Should().Contain("https://example.com/ai-news");
    }

    [Fact]
    public async Task Weather_clothing_question_answers_jacket_need_even_when_jacket_is_not_required()
    {
        using var weatherJson = JsonDocument.Parse("""
            {
              "current_condition": [
                {
                  "temp_F": "74",
                  "weatherDesc": [{ "value": "Clear" }]
                }
              ],
              "weather": [
                {
                  "date": "2026-05-04",
                  "maxtempF": "82",
                  "mintempF": "66",
                  "avgtempF": "74",
                  "hourly": [{ "weatherDesc": [{ "value": "Clear" }] }]
                },
                {
                  "date": "2026-05-05",
                  "maxtempF": "83",
                  "mintempF": "66",
                  "avgtempF": "74",
                  "hourly": [{ "weatherDesc": [{ "value": "Clear" }] }]
                }
              ]
            }
            """);
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "weather.get",
                Category = "public_data",
                Method = "GET",
                Endpoint = "external://weather",
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["tool"] = "weather.get",
                ["forecast_window"] = "tomorrow",
                ["detail_level"] = "clothing",
                ["response"] = weatherJson.RootElement.Clone()
            }),
            ledger);
        var context = CreateContext("whats teh wether tmrw do i need jaket", "weather.get");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeTrue();
        context.Answer.Should().Contain("Tomorrow (2026-05-05)");
        context.Answer.Should().Contain("74 F");
        context.Answer.Should().Contain("probably do not need a jacket");
        context.Answer.Should().Contain("Source: wttr.in");
        context.Answer.Should().NotContain("weather.get");
        context.Answer.Should().NotContain("Trace:");
    }

    [Fact]
    public async Task Blocked_policy_sets_blocked_by_policy_execution_state()
    {
        var ledger = new InMemoryLedgerRepository();
        var step = CreateStep(
            registryTool: new McpToolDefinition
            {
                Name = "workflow.start",
                Category = "workflow",
                Method = "POST",
                Endpoint = "connector://workflow/start",
                RequiresApproval = true,
                LogToLedger = true
            },
            executorResult: new McpExecutionResult(true, new Dictionary<string, object?>()),
            ledger);
        var context = CreateContext("start the workflow now", "workflow.start");

        await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        context.McpExecuted.Should().BeFalse();
        context.ExecutionState.Should().Be("blocked_by_policy");
    }

    private static AskMcpToolExecutionStep CreateStep(
        McpToolDefinition registryTool,
        McpExecutionResult executorResult,
        InMemoryLedgerRepository ledger)
    {
        return new AskMcpToolExecutionStep(
            new McpExecutionOptions
            {
                Enabled = true,
                ExecutionMode = "read_only",
                ReadOnlyAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "system.status",
                    "node.health.check",
                    "ledger.view",
                    "pack.list",
                    "pack.get",
                    "pin.resolve",
                    "weather.get",
                    "search.web"
                }
            },
            new FakeRegistryClient(new McpToolRegistryDocument
            {
                Tools = new[] { registryTool }
            }),
            new FakePolicyClient(new McpPolicyDocument
            {
                Rules = new[]
                {
                    new McpPolicyRule { Id = "require-ledger-logging", Enforce = true },
                    new McpPolicyRule { Id = "block-unauthorized-tools", Enforce = true }
                }
            }),
            new McpPolicyEvaluator(),
            new FakeExecutor(executorResult),
            new McpActionLedgerRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider())));
    }

    private static AskWorkflowContext CreateContext(string question, string intentType)
    {
        return new AskWorkflowContext(
            "tenant",
            "user",
            question,
            "personal",
            requestedPackAnchor: null,
            "standard",
            topK: 5,
            conversationId: "conv")
        {
            QueryClassification = "tool_intent_request",
            IntentType = intentType,
            TemplateUsed = "tool_readiness",
            RequestId = "req",
            TraceId = "trace"
        };
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

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.LastOrDefault(item => item.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events.Where(item => item.TraceId == traceId).ToArray());
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
}
