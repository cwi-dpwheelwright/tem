namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackDescriptor
{
    public string RequestedName { get; }
    public string ResolvedName { get; }
    public string Version { get; }
    public string Hash { get; }
    public string Anchor { get; }
    public PackContractType ContractType { get; }
    public IReadOnlyList<string> ArtifactLocations { get; }
    public IReadOnlyList<string> Entries { get; }
    public IReadOnlyList<string> Modules { get; }
    public string? PackTarget { get; }
    public string? PublicationSlug { get; }
    public string? PublicationVersion { get; }
    public string? PublicationPublishedFile { get; }
    public string? PublicationLatestFile { get; }

    public PackDescriptor(
        string requestedName,
        string resolvedName,
        string version,
        string hash,
        string anchor,
        PackContractType contractType,
        IReadOnlyList<string> artifactLocations,
        IReadOnlyList<string> entries,
        IReadOnlyList<string> modules,
        string? packTarget = null,
        string? publicationSlug = null,
        string? publicationVersion = null,
        string? publicationPublishedFile = null,
        string? publicationLatestFile = null)
    {
        RequestedName = requestedName;
        ResolvedName = resolvedName;
        Version = version;
        Hash = hash;
        Anchor = anchor;
        ContractType = contractType;
        ArtifactLocations = artifactLocations;
        Entries = entries;
        Modules = modules;
        PackTarget = packTarget;
        PublicationSlug = publicationSlug;
        PublicationVersion = publicationVersion;
        PublicationPublishedFile = publicationPublishedFile;
        PublicationLatestFile = publicationLatestFile;
    }
}
