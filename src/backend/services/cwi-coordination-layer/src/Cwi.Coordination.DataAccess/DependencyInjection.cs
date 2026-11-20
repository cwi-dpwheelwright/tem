using Cwi.Coordination.DataAccess.Repositories;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Coordination.DataAccess;

public static class DependencyInjection
{
    public static IServiceCollection AddCoordinationDataAccess(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<CoordinationDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRoundRepository, RoundRepository>();
        services.AddScoped<ILedgerRepository, LedgerRepository>();
        services.AddScoped<IMeshTopologyRepository, MeshTopologyRepository>();
        services.AddScoped<IMeshEventRepository, MeshEventRepository>();
        services.AddScoped<IBackhaulPolicyRepository, BackhaulPolicyRepository>();
        services.AddScoped<ISurvivalModeRepository, SurvivalModeRepository>();
        services.AddScoped<IDegradationEventRepository, DegradationEventRepository>();
        services.AddScoped<IPlacementMetricsRepository, PlacementMetricsRepository>();
        services.AddScoped<INodeWorkRepository, NodeWorkRepository>();

        return services;
    }
}
