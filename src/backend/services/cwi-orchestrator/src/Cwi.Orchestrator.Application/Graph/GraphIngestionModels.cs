#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Graph;

public sealed record GraphIngestionNode(
    string NodeId,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record GraphIngestionEdge(
    string EdgeId,
    string FromNodeId,
    string ToNodeId,
    string Relation,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record GraphIngestionProjection(
    IReadOnlyList<GraphIngestionNode> Nodes,
    IReadOnlyList<GraphIngestionEdge> Edges);

public sealed class GraphIngestionResult
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("skipped")]
    public bool Skipped { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("nodes_written")]
    public int NodesWritten { get; init; }

    [JsonPropertyName("edges_written")]
    public int EdgesWritten { get; init; }
}
#nullable disable
