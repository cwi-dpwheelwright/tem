namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackResolutionResult
{
    public PackDocument Root { get; }
    public IReadOnlyList<PackDocument> Dependencies { get; }
    public IReadOnlyList<PackDocument> BaselinePacks { get; }
    public PackCapabilityMap CapabilityMap { get; }

    public PackResolutionResult(
        PackDocument root,
        IReadOnlyList<PackDocument> dependencies,
        IReadOnlyList<PackDocument> baselinePacks,
        PackCapabilityMap capabilityMap)
    {
        Root = root;
        Dependencies = dependencies;
        BaselinePacks = baselinePacks;
        CapabilityMap = capabilityMap;
    }
}
