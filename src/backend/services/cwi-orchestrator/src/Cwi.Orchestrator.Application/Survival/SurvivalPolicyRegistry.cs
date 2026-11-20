namespace Cwi.Orchestrator.Application.Survival;

public sealed class SurvivalPolicyProfile
{
    public string Name { get; init; } = string.Empty;
    public double EnterThreshold { get; init; }
    public double RecoverThreshold { get; init; }
    public int CooldownMinutes { get; init; } = 10;
    public string OfflineMessage { get; init; } = "System in survival mode. Try again later.";
}

public sealed class SurvivalPolicyRegistry
{
    private readonly IReadOnlyList<SurvivalPolicyProfile> _profiles;

    public SurvivalPolicyRegistry()
    {
        _profiles = new List<SurvivalPolicyProfile>
        {
            new()
            {
                Name = "degraded",
                EnterThreshold = 0.7,
                RecoverThreshold = 0.4,
                CooldownMinutes = 10,
                OfflineMessage = "Service is degraded. Limited responses only."
            },
            new()
            {
                Name = "critical",
                EnterThreshold = 0.9,
                RecoverThreshold = 0.6,
                CooldownMinutes = 30,
                OfflineMessage = "Service in survival mode. Actions are disabled."
            }
        };
    }

    public SurvivalPolicyProfile Resolve(double severity)
    {
        return _profiles
            .OrderByDescending(profile => profile.EnterThreshold)
            .FirstOrDefault(profile => severity >= profile.EnterThreshold)
            ?? _profiles[0];
    }

    public IReadOnlyList<SurvivalPolicyProfile> Profiles => _profiles;
}
