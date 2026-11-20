namespace Cwi.Orchestrator.Application.Packs;

public static class CorePackOrdering
{
    public static readonly IReadOnlyList<string> CorePackOrder = new[]
    {
        "cwi-intent-interpreter",
        "cwi-tone-adaptation",
        "cwi-intent-to-tone-map",
        "cwi-audience-profiles",
        "cwi-memory-policy",
        "cwi-memory-capsule",
        "cwi-source-retrieval",
        "cwi-identity",
        "cwi-values-core",
        "cwi-values-biblical",
        "cwi-ethical-reasoner",
        "cwi-safety-guardrails",
        "cwi-confidence-policy"
    };

    public static readonly IReadOnlyList<string> FinalLayerOrder = new[]
    {
        "cwi-response-templates",
        "cwi-communication-core",
        "cwi-first-response-optimizer",
        "cwi-conversation-style-continuity",
        "cwi-response-refinement",
        "cwi-response-orchestrator"
    };

    public static readonly IReadOnlyList<string> RequiredPackOrder = CorePackOrder
        .Concat(FinalLayerOrder)
        .ToArray();

    public static IReadOnlyList<string> MergeRequiredPackNames(IEnumerable<string>? configuredPackNames)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packName in RequiredPackOrder)
        {
            AddIfPresent(packName, ordered, seen);
        }

        if (configuredPackNames is not null)
        {
            foreach (var packName in configuredPackNames)
            {
                AddIfPresent(packName, ordered, seen);
            }
        }

        return ordered;
    }

    public static int ResolveRequiredOrder(string packName)
    {
        for (var index = 0; index < RequiredPackOrder.Count; index++)
        {
            if (string.Equals(RequiredPackOrder[index], packName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    public static IReadOnlyList<string> PresentCorePacks(IEnumerable<string> packNames)
    {
        return PresentOrderedPacks(packNames, CorePackOrder);
    }

    public static IReadOnlyList<string> PresentFinalLayers(IEnumerable<string> packNames)
    {
        return PresentOrderedPacks(packNames, FinalLayerOrder);
    }

    public static bool IsCorePack(string packName)
    {
        return CorePackOrder.Contains(packName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsFinalLayer(string packName)
    {
        return FinalLayerOrder.Contains(packName, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> PresentOrderedPacks(
        IEnumerable<string> packNames,
        IReadOnlyList<string> preferredOrder)
    {
        var present = packNames
            .Where(pack => !string.IsNullOrWhiteSpace(pack))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return preferredOrder
            .Where(present.Contains)
            .ToArray();
    }

    private static void AddIfPresent(
        string? packName,
        List<string> ordered,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(packName))
        {
            return;
        }

        var normalized = packName.Trim();
        if (seen.Add(normalized))
        {
            ordered.Add(normalized);
        }
    }
}
