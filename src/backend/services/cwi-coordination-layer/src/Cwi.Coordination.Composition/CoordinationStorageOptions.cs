namespace Cwi.Coordination.Composition;

public sealed class CoordinationStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool UseInMemory { get; set; }
}
