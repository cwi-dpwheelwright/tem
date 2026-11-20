using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Pipeline;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.Application.Services.Decorators;
using Cwi.Coordination.Application.Transport;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Coordination.Composition;

public static class DependencyInjection
{
    public static IServiceCollection AddCoordinationLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new CoordinationCompositionOptions();
        configuration.GetSection("Coordination").Bind(options);
        services.AddSingleton(options);

        var storageOptions = new CoordinationStorageOptions();
        configuration.GetSection("Coordination:Storage").Bind(storageOptions);
        var envConnection = configuration["COORDINATION_DB_CONNECTION"];
        if (!string.IsNullOrWhiteSpace(envConnection))
        {
            storageOptions.ConnectionString = envConnection;
        }

        services.AddSingleton(storageOptions);

        var nodeSessionOptions = new NodeSessionOptions();
        configuration.GetSection("Coordination:NodeSessions").Bind(nodeSessionOptions);
        services.AddSingleton(nodeSessionOptions);

        var useInMemory = storageOptions.UseInMemory
            || string.IsNullOrWhiteSpace(storageOptions.ConnectionString);

        if (useInMemory)
        {
            services.AddSingleton<IMembershipRepository, InMemoryMembershipRepository>();
            services.AddSingleton<INodeWorkRepository, InMemoryNodeWorkRepository>();
            services.AddSingleton<IRoundRepository, InMemoryRoundRepository>();
            services.AddSingleton<ILedgerRepository, InMemoryLedgerRepository>();
            services.AddSingleton<IMeshTopologyRepository, InMemoryMeshTopologyRepository>();
            services.AddSingleton<IMeshEventRepository, InMemoryMeshEventRepository>();
            services.AddSingleton<IBackhaulPolicyRepository, InMemoryBackhaulPolicyRepository>();
            services.AddSingleton<ISurvivalModeRepository, InMemorySurvivalModeRepository>();
            services.AddSingleton<IDegradationEventRepository, InMemoryDegradationEventRepository>();
            services.AddSingleton<IPlacementMetricsRepository, InMemoryPlacementMetricsRepository>();
        }
        else
        {
            services.AddCoordinationDataAccess(storageOptions.ConnectionString);
        }

        var linkStateThresholds = new LinkStateThresholdOptions();
        configuration.GetSection("Coordination:LinkStateThresholds").Bind(linkStateThresholds);
        services.AddSingleton(linkStateThresholds);

        var meshEventOptions = new MeshEventQueryOptions();
        configuration.GetSection("Coordination:MeshEvents").Bind(meshEventOptions);
        services.AddSingleton(meshEventOptions);

        var topologyHealthOptions = new TopologyHealthOptions();
        configuration.GetSection("Coordination:TopologyHealth").Bind(topologyHealthOptions);
        services.AddSingleton(topologyHealthOptions);

        var linkMetricsOptions = new LinkMetricsNormalizationOptions();
        configuration.GetSection("Coordination:LinkMetrics").Bind(linkMetricsOptions);
        services.AddSingleton(linkMetricsOptions);

        var routingOptions = new RoutingHintOptions();
        configuration.GetSection("RoutingHints").Bind(routingOptions);
        services.AddSingleton(routingOptions);

        var placementOptions = new PlacementOptimizationOptions();
        configuration.GetSection("Coordination:PlacementOptimization").Bind(placementOptions);
        services.AddSingleton(placementOptions);

        services.AddSingleton<LedgerService>();
        services.AddScoped<LedgerQueryService>();
        services.AddScoped<LedgerVerificationService>();
        services.AddSingleton<LeaderElectionService>();
        services.AddSingleton<RoundOrchestrator>();
        services.AddSingleton<MeshTopologyService>();
        services.AddSingleton<LinkMetricsIngestService>();
        services.AddSingleton<RoutingHintService>();
        services.AddSingleton<TopologyHealthService>();
        services.AddSingleton<RerouteGuidanceService>();
        services.AddSingleton<BackhaulPolicyService>();
        services.AddSingleton<ActivationStatusService>();
        services.AddSingleton<DegradationEventService>();
        services.AddSingleton<SurvivalModeService>();
        services.AddSingleton<PlacementMetricsService>();
        services.AddSingleton<PlacementRecommendationService>();
        services.AddSingleton<IRoutingHintEngine, DefaultRoutingHintEngine>();
        services.AddSingleton<ILinkMetricsTransportAdapter, SimulatedLinkMetricsTransportAdapter>();
        services.AddSingleton<MembershipPipeline>(sp =>
        {
            var handlers = new IPipelineHandler<Application.Models.EnrollmentRequest, Application.Models.EnrollmentResponse>[]
            {
                new MembershipValidationHandler(),
                new MembershipAuthnHandler(),
                new MembershipAuthzHandler(),
                new MembershipStateMachineHandler(),
                new MembershipAuditHandler()
            };
            return new MembershipPipeline(handlers);
        });

        services.AddSingleton<IMetricsRecorder, NoopMetricsRecorder>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<INodeSessionDispatcher>(NoopNodeSessionDispatcher.Instance);

        services.AddScoped<IMembershipService>(sp =>
        {
            var baseService = new MembershipService(
                sp.GetRequiredService<IMembershipRepository>(),
                sp.GetRequiredService<MembershipPipeline>());
            var withIdempotency = new MembershipIdempotencyDecorator(baseService, sp.GetRequiredService<IIdempotencyStore>());
            var withMetrics = new MembershipMetricsDecorator(withIdempotency, sp.GetRequiredService<IMetricsRecorder>());
            return new MembershipAuditDecorator(withMetrics, sp.GetRequiredService<LedgerService>());
        });

        services.AddScoped<NodeSessionMembershipService>();
        services.AddScoped<RoundService>();
        services.AddScoped<NodeWorkService>();

        services.AddHttpClient<NodeTransportClient>();

        services.AddSingleton<ApplicationOptions>(sp => new ApplicationOptions
        {
            ProtocolVersion = options.ProtocolVersion,
            RequireClientCertificate = options.RequireClientCertificate
        });

        return services;
    }
}

internal sealed class NoopMetricsRecorder : IMetricsRecorder
{
    public void Increment(string name)
    {
    }
}

internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly HashSet<string> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public bool TryBegin(string key)
    {
        lock (_inflight)
        {
            if (_inflight.Contains(key))
            {
                return false;
            }
            _inflight.Add(key);
            return true;
        }
    }

    public void Complete(string key)
    {
        lock (_inflight)
        {
            _inflight.Remove(key);
        }
    }
}
