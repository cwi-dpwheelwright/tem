namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackManifestMetadata
{
    public string? DeclaredName { get; }
    public string? PackFamily { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public IReadOnlyList<PackCapability> Capabilities { get; }
    public IReadOnlyList<string> Entries { get; }
    public IReadOnlyList<string> Modules { get; }
    public IReadOnlyList<string> ModuleRoles { get; }
    public IReadOnlyList<string> ScopeAppliesTo { get; }
    public IReadOnlyList<string> ForceMatchQueries { get; }
    public int? ActivationLoadOrder { get; }
    public bool AlwaysLoad { get; }
    public bool RuntimeRequired { get; }
    public float? ConfidenceFloor { get; }
    public string? ApplyStage { get; }
    public string? HookName { get; }

    public PackManifestMetadata(
        string? declaredName,
        string? packFamily,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<PackCapability> capabilities,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> modules,
        IReadOnlyList<string> moduleRoles,
        IReadOnlyList<string> scopeAppliesTo,
        IReadOnlyList<string> forceMatchQueries,
        int? activationLoadOrder,
        bool alwaysLoad,
        bool runtimeRequired,
        float? confidenceFloor,
        string? applyStage,
        string? hookName)
    {
        DeclaredName = declaredName;
        PackFamily = packFamily;
        Dependencies = dependencies;
        Capabilities = capabilities;
        Entries = entries;
        Modules = modules;
        ModuleRoles = moduleRoles;
        ScopeAppliesTo = scopeAppliesTo;
        ForceMatchQueries = forceMatchQueries;
        ActivationLoadOrder = activationLoadOrder;
        AlwaysLoad = alwaysLoad;
        RuntimeRequired = runtimeRequired;
        ConfidenceFloor = confidenceFloor;
        ApplyStage = applyStage;
        HookName = hookName;
    }

    public bool HasModuleRole(string role)
    {
        return ModuleRoles.Any(candidate =>
            string.Equals(candidate, role, StringComparison.OrdinalIgnoreCase));
    }

    public bool AppliesAtStage(string stage)
    {
        return !string.IsNullOrWhiteSpace(stage)
               && string.Equals(ApplyStage, stage, StringComparison.OrdinalIgnoreCase);
    }

    public bool MatchesForceQuery(string? question)
    {
        if (string.IsNullOrWhiteSpace(question) || ForceMatchQueries.Count == 0)
        {
            return false;
        }

        var normalizedQuestion = Normalize(question);
        return ForceMatchQueries.Any(pattern =>
        {
            var normalizedPattern = Normalize(pattern);
            return !string.IsNullOrWhiteSpace(normalizedPattern)
                   && normalizedQuestion.Contains(normalizedPattern, StringComparison.Ordinal);
        });
    }

    private static string Normalize(string value)
    {
        var buffer = new char[value.Length];
        var index = 0;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            buffer[index++] = char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' ';
        }

        return string.Join(' ', new string(buffer, 0, index)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
