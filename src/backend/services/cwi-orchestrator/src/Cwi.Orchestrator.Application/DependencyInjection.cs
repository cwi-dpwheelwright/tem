using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Decorators;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Application.Http;
using Microsoft.Extensions.DependencyInjection;
using Cwi.Orchestrator.Application.Feedback;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Guardrails.Validators.Library;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Guardrails;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Provenance;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Packs.Provenance;
using Cwi.Orchestrator.Application.Reconciliation;
using Cwi.Orchestrator.Application.Survival;
using Cwi.Orchestrator.Application.Survival.Provenance;
using Cwi.Orchestrator.Application.Simulation;
using Cwi.Orchestrator.Application.Inference;
using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Application.Dev;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Federated;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Reports;
using Cwi.Orchestrator.Application.OperatorMemory;
using Cwi.Orchestrator.Application.Artifacts;
using Cwi.Orchestrator.Application.History;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Connectors;
using Cwi.Orchestrator.Application.Mcp;
using Cwi.Orchestrator.Application.Recursive;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Repositories;
using Cwi.Common.Journey;
using System.Net.Http.Headers;

namespace Cwi.Orchestrator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestratorApplication(
        this IServiceCollection services,
        ApplicationOptions options)
    {
        services.AddSingleton(options);
        services.AddMemoryCache();
        services.AddSingleton<JourneyEventBusPublisher>();
        services.AddScoped<JourneyRequestContext>();
        services.AddScoped<JourneyEmitter>();
        services.AddScoped<IJourneyService, JourneyService>();
        services.AddScoped<JourneyStreamIngestService>();
        services.AddTransient<ResilienceHandler>();
        services.AddSingleton(LanguageNormalizationOptions.LoadFromEnvironment());
        services.AddSingleton<LocalLanguageNormalizationClient>();
        services.AddHttpClient<HttpLanguageNormalizationClient>((provider, client) =>
        {
            var normalizationOptions = provider.GetRequiredService<LanguageNormalizationOptions>();
            if (!string.IsNullOrWhiteSpace(normalizationOptions.BaseUrl))
            {
                client.BaseAddress = new Uri(normalizationOptions.BaseUrl.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromMilliseconds(normalizationOptions.TimeoutMs);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddTransient<FallbackLanguageNormalizationClient>();
        services.AddTransient<ILanguageNormalizationClient>(provider =>
        {
            var normalizationOptions = provider.GetRequiredService<LanguageNormalizationOptions>();
            return normalizationOptions.Mode switch
            {
                LanguageNormalizationMode.External => provider.GetRequiredService<HttpLanguageNormalizationClient>(),
                LanguageNormalizationMode.ExternalWithLocalFallback => provider.GetRequiredService<FallbackLanguageNormalizationClient>(),
                _ => provider.GetRequiredService<LocalLanguageNormalizationClient>()
            };
        });
        services.AddHttpClient("packs")
            .AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient<VectorShard1Client>(client =>
        {
            client.BaseAddress = new Uri(options.VectorShard1Url);
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient<VectorShard2Client>(client =>
        {
            client.BaseAddress = new Uri(options.VectorShard2Url);
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient("model", client =>
        {
            client.BaseAddress = new Uri(options.ModelBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.ModelTimeoutSeconds);
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddSingleton(McpExecutionOptions.LoadFromEnvironment());
        services.AddSingleton(ActivatedMemoryOptions.LoadFromEnvironment());
        services.AddHttpClient("mcp", (provider, client) =>
        {
            var mcpOptions = provider.GetRequiredService<McpExecutionOptions>();
            client.Timeout = TimeSpan.FromSeconds(mcpOptions.HttpTimeoutSeconds <= 0 ? 10 : mcpOptions.HttpTimeoutSeconds);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddSingleton(FeedbackReportOptions.LoadFromEnvironment());
        services.AddHttpClient("smartsheet-feedback", (provider, client) =>
        {
            var feedbackOptions = provider.GetRequiredService<FeedbackReportOptions>();
            client.BaseAddress = new Uri(feedbackOptions.SmartsheetBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddSingleton(TrustedWebContextOptions.LoadFromEnvironment());
        services.AddSingleton(SelectiveEnforcementOptions.LoadFromEnvironment());
        services.AddSingleton(SustainabilityMetricsOptions.LoadFromEnvironment());
        services.AddSingleton(FederatedLearningSummaryOptions.LoadFromEnvironment());
        services.AddSingleton(ExternalOpinionOptions.LoadFromEnvironment());
        services.AddSingleton(RuntimeCriticOptions.LoadFromEnvironment());
        services.AddHttpClient("trusted-web", (provider, client) =>
        {
            var trustedWebOptions = provider.GetRequiredService<TrustedWebContextOptions>();
            client.Timeout = TimeSpan.FromMilliseconds(trustedWebOptions.FetchTimeoutMs);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient<HomeNodeDispatchClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.CoordinationBaseUrl))
            {
                client.BaseAddress = new Uri(options.CoordinationBaseUrl);
            }

            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient("teacher-node", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient("external-opinion", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddHttpClient<ExternalRuntimeCriticClient>((provider, client) =>
        {
            var criticOptions = provider.GetRequiredService<RuntimeCriticOptions>();
            if (!string.IsNullOrWhiteSpace(criticOptions.BaseUrl))
            {
                client.BaseAddress = new Uri(criticOptions.BaseUrl.TrimEnd('/') + "/");
            }

            client.Timeout = TimeSpan.FromMilliseconds(criticOptions.TimeoutMs <= 0 ? 1500 : criticOptions.TimeoutMs);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<ResilienceHandler>();
        services.AddSingleton<ModelTargetResolver>();
        services.AddTransient<Packs.PackFetcher>();
        services.AddTransient<IPackSignatureValidator, NoopPackSignatureValidator>();
        services.AddTransient<IPackContractResolver, AdoptedPackResolver>();
        services.AddTransient<IPackContractResolver, LegacyPackResolver>();
        services.AddSingleton<ICorePackRuntimeCatalog, CorePackRuntimeCatalog>();
        services.AddHostedService<CorePackRefreshHostedService>();
        services.AddTransient<IPackResolver, PackResolver>();
        services.AddTransient<BaselinePackContentProvider>();
        services.AddSingleton<GraphQueryPlanner>();
        services.AddTransient<AgeGraphEvidenceProvider>();
        services.AddTransient<IGraphEvidenceProvider>(provider =>
            new CandidateAwareGraphEvidenceProvider(
                provider.GetRequiredService<AgeGraphEvidenceProvider>(),
                provider.GetRequiredService<IGraphUpdateCandidateRepository>()));
        services.AddSingleton<ImportedChatGraphProjectionBuilder>();
        services.AddSingleton<ConnectorGraphProjectionBuilder>();
        services.AddTransient<IImportedChatGraphIngestionService, AgeImportedChatGraphIngestionService>();
        services.AddTransient<GraphQueryService>();
        services.AddScoped<GraphToVectorRefreshService>();
        services.AddScoped<GraphEvolutionService>();
        services.AddTransient<UserGraphProfileService>();
        services.AddTransient<UserIntelligenceService>();
        services.AddSingleton<PackCapabilityPolicyEvaluator>();
        services.AddScoped<IPackCapabilityLedgerRecorder, PackCapabilityLedgerRecorder>();
        services.AddTransient<PromptBuilder>();
        services.AddSingleton<AskQueryClassifier>();
        services.AddSingleton<ComprehensionExpressionFrameworkService>();
        services.AddSingleton<PresentationFrameworkService>();
        services.AddSingleton<ChatResponseQualityService>();
        services.AddSingleton<ResponseIntelligenceOverlayService>();
        services.AddTransient<AskService>();
        services.AddTransient<ActionService>();
        services.AddTransient<ApprovalService>();
        services.AddTransient<ApprovalQueryService>();
        services.AddTransient<IngestDocumentService>();
        services.AddTransient<TelemetryIngestService>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Ingest.PackIngestService>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Lifecycle.PackLifecycleService>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Lifecycle.PackLifecycleAuditRecorder>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Lifecycle.PackAuditService>();
        services.AddSingleton<IngestDocumentSchemaValidator>();
        services.AddSingleton<Cwi.Orchestrator.Application.Packs.Ingest.PackSchemaValidator>();
        services.AddSingleton<TelemetrySchemaValidator>();
        services.AddSingleton<RecursiveEventSchemaValidator>();
        services.AddScoped<RecursiveEventIngestService>();
        services.AddSingleton<OrchestratorMetrics>();
        services.AddSingleton<SustainabilityCalculator>();
        services.AddScoped<SustainabilityMetricsService>();
        services.AddScoped<FederatedLearningSummaryService>();
        services.AddScoped<ExternalOpinionService>();
        services.AddSingleton<RuntimeCapabilityStatusService>();
        services.AddTransient<IRuntimeCriticClient>(provider => provider.GetRequiredService<ExternalRuntimeCriticClient>());
        services.AddScoped<RuntimeCriticLedgerRecorder>();
        services.AddSingleton<TruthEngineService>();
        services.AddScoped<TruthEngineLedgerRecorder>();
        services.AddScoped<ResponseQualityGraphService>();
        services.AddScoped<ValidatedCacheService>();
        services.AddSingleton<TraceLookupStore>();
        services.AddSingleton<TelemetryIndexStore>();
        services.AddSingleton<OutcomeFeedbackSignalStore>();
        services.AddSingleton<DocumentStore>();
        services.AddSingleton<ReportStore>();
        services.AddScoped<ArtifactRegistryService>();
        services.AddScoped<HomeNodeRegistryService>();
        services.AddScoped<HomeNodeAssignmentService>();
        services.AddScoped<HomeNodePolicyScopeService>();
        services.AddScoped<CurrentUserHomeNodeLinkService>();
        services.AddSingleton(AccountConnectorOAuthOptions.LoadFromEnvironment());
        services.AddScoped<AccountConnectorService>();
        services.AddSingleton<ConnectorNormalizationService>();
        services.AddScoped<ConnectorWebhookIngestionService>();
        services.AddScoped<HomeNodeWorkTokenService>();
        services.AddScoped<HomeNodeDispatchService>();
        services.AddScoped<HomeNodeDeferredAskService>();
        services.AddSingleton(new OperatorMemoryOptions());
        services.AddSingleton<OperatorMemoryRateLimiter>();
        services.AddSingleton<OperatorMemoryRedactionPolicy>();
        services.AddSingleton<OperatorPreferenceMemory>();
        services.AddScoped<OperatorProfileService>();
        services.AddSingleton<OperatorProfilePolicyService>();
        services.AddScoped<OperatorMemoryIngestService>();
        services.AddScoped<IOutcomeFeedbackService, OutcomeFeedbackService>();
        services.AddScoped<FeedbackReportService>();
        services.AddTransient<ISmartsheetFeedbackClient, SmartsheetFeedbackClient>();
        services.AddScoped<IAskHistoryService, AskHistoryService>();
        services.AddSingleton<ActivatedMemoryArtifactProvider>();
        services.AddSingleton<MemoryFrameworkService>();
        services.AddScoped<ChatHistoryMemoryActivationService>();
        services.AddScoped<GovernedMemoryService>();
        services.AddScoped<TrustedWebConfigService>();
        services.AddScoped<ITrustedWebConfigStore>(provider => provider.GetRequiredService<TrustedWebConfigService>());
        services.AddTransient<ITrustedWebContextProvider, ConfiguredTrustedWebContextProvider>();
        services.AddScoped<ChatImportService>();
        services.AddSingleton<IGuardrailValidator, BoundsValidator>();
        services.AddSingleton<IGuardrailValidator, DimensionCompatibilityValidator>();
        services.AddSingleton<GuardrailValidatorRegistry>();
        services.AddSingleton(new SeverityConfig());
        services.AddSingleton<SeverityScorer>();
        services.AddSingleton<GuardrailEvaluator>();
        services.AddSingleton(new GuardrailDecisionConfig());
        services.AddSingleton<GuardrailDecisionMapper>();
        services.AddSingleton(new GuardrailCorrectionOptions());
        services.AddSingleton<GuardrailCorrectionEngine>();
        services.AddSingleton(new SimulationOptions());
        services.AddSingleton<ISimulationFeasibilityEvaluator, SimulationFeasibilityEvaluator>();
        services.AddSingleton<DiscrepancyEvaluator>();
        services.AddSingleton<ICorrectionGenerator, BaselineCorrectionGenerator>();
        services.AddSingleton<IFallbackSelector, DiscrepancyFallbackSelector>();
        services.AddScoped<InferenceLedgerRecorder>();
        services.AddScoped<DiscrepancyApprovalService>();
        services.AddScoped<IGuardrailLedgerRecorder, GuardrailLedgerRecorder>();
        services.AddScoped<SurvivalLedgerRecorder>();
        services.AddSingleton<ISignatureProvider, NoopSignatureProvider>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<ApplicationOptions>();
            return new LedgerHashOptions { SuiteId = options.LedgerHashSuiteId };
        });
        services.AddSingleton(provider =>
        {
            var signatureProvider = provider.GetRequiredService<ISignatureProvider>();
            var hashOptions = provider.GetRequiredService<LedgerHashOptions>();
            var policy = provider.GetRequiredService<LedgerRedactionPolicy>();
            return new LedgerEventFactory(signatureProvider, hashOptions.SuiteId, policy);
        });
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<LedgerRedactionOptions>();
            return new LedgerRedactionPolicy(options.Enabled, options.Fields);
        });
        services.AddScoped<LedgerTombstoneService>();
        services.AddScoped<LedgerVerificationService>();
        services.AddScoped<LedgerExportService>();
        services.AddScoped<LedgerEventQueryService>();
        services.AddTransient<PolicyDefaultsResolver>();
        services.AddTransient<KillSwitchEvaluator>();
        services.AddSingleton<KillSwitchTokenStore>();
        services.AddTransient<KillSwitchAdminService>();
        services.AddSingleton<IPolicyEvaluator, PolicyEvaluator>();
        services.AddSingleton<McpPolicyEvaluator>();
        services.AddSingleton<McpConnectorCatalog>();
        services.AddSingleton<IMcpRegistryClient, HttpMcpRegistryClient>();
        services.AddSingleton<IMcpPolicyClient, HttpMcpPolicyClient>();
        services.AddScoped<IMcpToolExecutor, GovernedMcpToolExecutor>();
        services.AddScoped<McpActionLedgerRecorder>();
        services.AddSingleton<IEmbeddingProvider>(_ => new HashEmbeddingProvider(options.VectorEmbeddingDimension));
        services.AddTransient<IRetrievalProvider, VectorShardRetrievalProvider>();
        services.AddTransient<IModelClient>(provider =>
        {
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var resolver = provider.GetRequiredService<ModelTargetResolver>();
            return new OllamaModelClient(options, factory, resolver);
        });
        services.AddTransient<IAskWorkflowDecorator, AskIdempotencyDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskKillSwitchDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskProvenanceDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskSurvivalLedgerDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskPackCapabilityLedgerDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskMetricsDecorator>();
        services.AddTransient<IAskWorkflowDecorator, AskRuntimeCriticDecorator>();
        services.AddTransient<IAskWorkflowStep, AskNormalizationVerificationRouteStep>();
        services.AddTransient<IAskWorkflowStep, AskDeterministicDirectRouteStep>();
        services.AddTransient<IAskWorkflowStep, AskEntityLookupRouteStep>();
        services.AddTransient<IAskWorkflowStep, AskPackResolveStep>();
        services.AddTransient<IAskWorkflowStep, AskBaselinePackGuidanceStep>();
        services.AddTransient<IAskWorkflowStep, AskQueryClassificationStep>();
        services.AddTransient<IAskWorkflowStep, AskCoreGovernanceStep>();
        services.AddTransient<IAskWorkflowStep, AskPreferenceMemoryStep>();
        services.AddTransient<IAskWorkflowStep, AskCommunicationRuntimeStep>();
        services.AddTransient<IAskWorkflowStep, AskProactiveIntelligenceStep>();
        services.AddTransient<IAskWorkflowStep, AskMcpToolExecutionStep>();
        services.AddTransient<IAskWorkflowStep, AskDirectAnswerStep>();
        services.AddTransient<IAskWorkflowStep, AskPolicyModeStep>();
        services.AddTransient<IAskWorkflowStep, AskOperatorProfileStep>();
        services.AddTransient<IAskWorkflowStep, AskPackCapabilityStep>();
        services.AddTransient<IAskWorkflowStep, AskSurvivalStatusStep>();
        services.AddTransient<IAskWorkflowStep, AskHomeNodeDispatchStep>();
        services.AddTransient<IAskWorkflowStep, AskDocumentMemoryRetrievalStep>();
        services.AddTransient<IAskWorkflowStep, AskGraphRetrievalStep>();
        services.AddTransient<IAskWorkflowStep, AskMemoryProfileAnswerStep>();
        services.AddTransient<IAskWorkflowStep, AskRetrievalStep>();
        services.AddTransient<IAskWorkflowStep, AskEvidenceRequirementStep>();
        services.AddTransient<IAskWorkflowStep, AskTrustedWebContextStep>();
        services.AddTransient<IAskWorkflowStep, AskTruthEngineStep>();
        services.AddTransient<IAskWorkflowStep, AskPromptStep>();
        services.AddTransient<IAskWorkflowStep, AskInferenceStep>();
        services.AddTransient<IAskWorkflowStep, AskGuardrailsStep>();
        services.AddTransient<IAskWorkflowStep, AskDiscrepancyStep>();
        services.AddTransient<IAskWorkflowStep, AskFallbackResponseStep>();
        services.AddTransient<IAskWorkflowStep, AskFinalCompositionStep>();
        services.AddTransient<IAskWorkflowStep, AskResponseOrchestrationStep>();
        services.AddTransient<IAskWorkflowStep, AskMemoryUpdateStep>();
        services.AddTransient<IAskWorkflowStep, AskResponseStep>();
        services.AddTransient<IActionWorkflowStep, ActionPolicyModeStep>();
        services.AddTransient<IActionWorkflowStep, ActionSurvivalStatusStep>();
        services.AddTransient<IActionWorkflowStep, ActionSimulationFeasibilityStep>();
        services.AddTransient<IActionWorkflowStep, ActionGuardrailsStep>();
        services.AddTransient<IActionWorkflowStep, ActionMcpRegistryPolicyStep>();
        services.AddTransient<IActionWorkflowStep, ActionDecisionStep>();
        services.AddTransient<IActionWorkflowStep, ActionMcpExecutionStep>();
        services.AddTransient<IActionWorkflowStep, ActionResponseStep>();
        services.AddTransient<IActionWorkflowDecorator, ActionIdempotencyDecorator>();
        services.AddTransient<IActionWorkflowDecorator, ActionKillSwitchDecorator>();
        services.AddTransient<IActionWorkflowDecorator, ActionProvenanceDecorator>();
        services.AddTransient<IActionWorkflowDecorator, ActionSurvivalLedgerDecorator>();
        services.AddTransient<IActionWorkflowDecorator, ActionMetricsDecorator>();
        services.AddTransient<ITelemetryIngestWorkflowDecorator, TelemetryIngestIdempotencyDecorator>();
        services.AddTransient<ITelemetryIngestWorkflowDecorator, TelemetryIngestKillSwitchDecorator>();
        services.AddTransient<ITelemetryIngestWorkflowDecorator, TelemetryIngestProvenanceDecorator>();
        services.AddTransient<ITelemetryIngestWorkflowDecorator, TelemetryIngestMetricsDecorator>();
        services.AddTransient<ITelemetryIngestWorkflowStep, TelemetryIngestAcceptStep>();
        services.AddTransient<ITelemetryIngestWorkflowStep, TelemetryIngestPersistStep>();
        services.AddTransient<ITelemetryIngestWorkflowStep, TelemetryIngestResponseStep>();
        services.AddTransient<AskWorkflowPipeline>();
        services.AddTransient<ActionWorkflowPipeline>();
        services.AddTransient<TelemetryIngestWorkflowPipeline>();
        services.AddSingleton<ConflictDetector>();
        services.AddSingleton<RecordReconciler>();
        services.AddScoped<ReconciliationApprovalService>();
        services.AddScoped<ReconciliationApprovalResolutionService>();
        services.AddScoped<CanonicalRecordService>();
        services.AddSingleton<IOwnershipPolicyEvaluator, OwnershipPolicyEvaluator>();
        services.AddScoped<OwnershipTransferService>();
        services.AddScoped<OwnershipGraphService>();
        services.AddScoped<OwnershipLineageBindingService>();
        services.AddScoped<SuccessionService>();
        services.AddSingleton<SurvivalPolicyRegistry>();
        services.AddSingleton<SurvivalSignalStore>();
        services.AddSingleton<SurvivalEvaluator>();
        services.AddScoped<SurvivalService>();
        services.AddTransient<FaultInjectionService>();
        return services;
    }
}

