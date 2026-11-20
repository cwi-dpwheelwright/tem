namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackCapabilityMap
{
    public IReadOnlyList<PackCapability> Capabilities { get; }
    public IReadOnlyDictionary<string, PackCapability> ByName { get; }

    public PackCapabilityMap(IReadOnlyList<PackCapability> capabilities)
    {
        Capabilities = capabilities;
        var map = new Dictionary<string, PackCapability>(StringComparer.OrdinalIgnoreCase);
        foreach (var capability in capabilities)
        {
            if (!map.ContainsKey(capability.Name))
            {
                map[capability.Name] = capability;
            }
        }

        ByName = map;
    }
}
