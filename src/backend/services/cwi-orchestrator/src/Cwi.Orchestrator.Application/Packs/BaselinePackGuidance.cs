namespace Cwi.Orchestrator.Application.Packs;

public sealed class BaselinePackGuidance
{
    public string PackName { get; }
    public string PackVersion { get; }
    public string PackHash { get; }
    public string Content { get; }
    public string? PackFamily { get; }
    public string? ModuleRole { get; }
    public string? ApplyStage { get; }
    public string? HookName { get; }
    public bool AlwaysLoad { get; }
    public bool RuntimeRequired { get; }
    public IReadOnlyList<string> ForceMatchQueries { get; }
    public float? ConfidenceFloor { get; }

    public BaselinePackGuidance(
        string packName,
        string packVersion,
        string packHash,
        string content,
        string? packFamily = null,
        string? moduleRole = null,
        string? applyStage = null,
        string? hookName = null,
        bool alwaysLoad = false,
        bool runtimeRequired = false,
        IReadOnlyList<string>? forceMatchQueries = null,
        float? confidenceFloor = null)
    {
        PackName = packName;
        PackVersion = packVersion;
        PackHash = packHash;
        Content = content;
        PackFamily = packFamily;
        ModuleRole = moduleRole;
        ApplyStage = applyStage;
        HookName = hookName;
        AlwaysLoad = alwaysLoad;
        RuntimeRequired = runtimeRequired;
        ForceMatchQueries = forceMatchQueries ?? Array.Empty<string>();
        ConfidenceFloor = confidenceFloor;
    }

    public bool HasRole(string role)
    {
        return !string.IsNullOrWhiteSpace(role)
               && string.Equals(ModuleRole, role, StringComparison.OrdinalIgnoreCase);
    }

    public bool AppliesAtStage(string stage)
    {
        return !string.IsNullOrWhiteSpace(stage)
               && string.Equals(ApplyStage, stage, StringComparison.OrdinalIgnoreCase);
    }
}
