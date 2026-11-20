namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackDocument
{
    public string Name { get; }
    public string Version { get; }
    public string Json { get; }
    public string Anchor { get; }
    public string Hash { get; }
    public PackContractType ContractType { get; }
    public PackDescriptor Descriptor { get; }

    public PackDocument(
        string name,
        string version,
        string json,
        string anchor,
        string hash,
        PackContractType contractType,
        PackDescriptor descriptor)
    {
        Name = name;
        Version = version;
        Json = json;
        Anchor = anchor;
        Hash = hash;
        ContractType = contractType;
        Descriptor = descriptor;
    }
}
