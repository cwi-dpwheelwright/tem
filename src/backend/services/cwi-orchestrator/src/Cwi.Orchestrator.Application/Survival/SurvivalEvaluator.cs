using System.Linq;

namespace Cwi.Orchestrator.Application.Survival;

public sealed class SurvivalEvaluator
{
    private readonly SurvivalPolicyRegistry _registry;
    private readonly SurvivalSignalStore _store;
    private readonly object _sync = new();
    private SurvivalStatusDto? _lastStatus;

    public SurvivalEvaluator(SurvivalPolicyRegistry registry, SurvivalSignalStore store)
    {
        _registry = registry;
        _store = store;
    }

    public SurvivalStatusDto Evaluate()
    {
        lock (_sync)
        {
            var latest = _store.GetLatest();
            if (latest is null)
            {
                _lastStatus = new SurvivalStatusDto
                {
                    Mode = "normal",
                    Profile = "",
                    LastSignalUtc = null,
                    OfflineMessage = string.Empty
                };
                return _lastStatus;
            }

            var resolvedProfile = _registry.Resolve(latest.Severity);
            var previousProfile = ResolveProfileByName(_lastStatus?.Profile) ?? resolvedProfile;

            var elapsed = DateTimeOffset.UtcNow - latest.ObservedUtc;
            var isExpired = elapsed > TimeSpan.FromMinutes(previousProfile.CooldownMinutes);

            var criticalProfile = _registry.Profiles
                .OrderByDescending(profile => profile.EnterThreshold)
                .First();
            var degradedProfile = _registry.Profiles
                .OrderBy(profile => profile.EnterThreshold)
                .First();

            var mode = ResolveMode(latest.Severity, isExpired, criticalProfile, degradedProfile);
            var profileForStatus = mode switch
            {
                "survival" => criticalProfile,
                "degraded" => degradedProfile,
                _ => previousProfile
            };

            _lastStatus = new SurvivalStatusDto
            {
                Mode = mode,
                Profile = profileForStatus.Name,
                LastSignalUtc = latest.ObservedUtc,
                OfflineMessage = mode == "normal" ? string.Empty : profileForStatus.OfflineMessage
            };
            return _lastStatus;
        }
    }

    private string ResolveMode(
        double severity,
        bool isExpired,
        SurvivalPolicyProfile criticalProfile,
        SurvivalPolicyProfile degradedProfile)
    {
        if (isExpired)
        {
            return "normal";
        }

        var previousMode = _lastStatus?.Mode ?? "normal";

        if (string.Equals(previousMode, "survival", StringComparison.OrdinalIgnoreCase))
        {
            if (severity <= criticalProfile.RecoverThreshold)
            {
                return severity >= degradedProfile.EnterThreshold ? "degraded" : "normal";
            }

            return "survival";
        }

        if (string.Equals(previousMode, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            if (severity <= degradedProfile.RecoverThreshold)
            {
                return "normal";
            }

            return "degraded";
        }

        if (severity >= criticalProfile.EnterThreshold)
        {
            return "survival";
        }

        if (severity >= degradedProfile.EnterThreshold)
        {
            return "degraded";
        }

        return "normal";
    }

    private SurvivalPolicyProfile? ResolveProfileByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _registry.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
