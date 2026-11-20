using Cwi.Orchestrator.DataAccess.Health;
using Cwi.Orchestrator.Domain.Health;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Orchestrator.DataAccess;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestratorDataAccess(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OrchestratorDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<ITenantConfigRepository, Repositories.TenantConfigRepository>();
        services.AddScoped<IApprovalTicketRepository, Repositories.ApprovalTicketRepository>();
        services.AddScoped<ILedgerRepository, Repositories.LedgerRepository>();
        services.AddScoped<ITelemetryWindowRepository, Repositories.TelemetryWindowRepository>();
        services.AddScoped<IConnectorRunRepository, Repositories.ConnectorRunRepository>();
        services.AddScoped<IBreakGlassTokenRepository, Repositories.BreakGlassTokenRepository>();
        services.AddScoped<IPackActivationRepository, Repositories.PackActivationRepository>();
        services.AddScoped<ICanonicalRecordRepository, Repositories.CanonicalRecordRepository>();
        services.AddScoped<IOwnershipTransferRepository, Repositories.OwnershipTransferRepository>();
        services.AddScoped<IOutcomeFeedbackRepository, Repositories.OutcomeFeedbackRepository>();
        services.AddScoped<IOperatorProfileRepository, Repositories.OperatorProfileRepository>();
        services.AddScoped<IOwnershipLineageRepository, Repositories.OwnershipLineageRepository>();
        services.AddScoped<ISuccessionPlanRepository, Repositories.SuccessionPlanRepository>();
        services.AddScoped<IArtifactRegistryRepository, Repositories.ArtifactRegistryRepository>();
        services.AddScoped<IAskHistoryRepository, Repositories.AskHistoryRepository>();
        services.AddScoped<IJourneyEventRepository, Repositories.JourneyEventRepository>();
        services.AddScoped<IHomeNodeRepository, Repositories.HomeNodeRepository>();
        services.AddScoped<IHomeNodeAssignmentRepository, Repositories.HomeNodeAssignmentRepository>();
        services.AddScoped<IHomeNodePolicyScopeRepository, Repositories.HomeNodePolicyScopeRepository>();
        services.AddScoped<IGovernedMemoryRepository, Repositories.GovernedMemoryRepository>();
        services.AddScoped<ISourceMemoryRepository, Repositories.SourceMemoryRepository>();
        services.AddScoped<IUserConnectorConnectionRepository, Repositories.UserConnectorConnectionRepository>();
        services.AddScoped<IConnectorIngestionRepository, Repositories.ConnectorIngestionRepository>();
        services.AddScoped<IRecursiveEventRepository, Repositories.RecursiveEventRepository>();
        services.AddScoped<IGraphUpdateCandidateRepository, Repositories.GraphUpdateCandidateRepository>();
        services.AddScoped<IGraphVectorRefreshJobRepository, Repositories.GraphVectorRefreshJobRepository>();
        services.AddScoped<IValidatedCacheRepository, Repositories.ValidatedCacheRepository>();
        services.AddScoped<ISustainabilityMetricsRepository, Repositories.SustainabilityMetricsRepository>();
        services.AddScoped<IProvenanceHealthChecker, ProvenanceHealthChecker>();

        return services;
    }
}
