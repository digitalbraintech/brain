namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.graph-state")]
public sealed record GraphState(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<GraphNodeState> Nodes,
    [property: Id(2)] IReadOnlyList<GraphEdgeState> Edges);

[GenerateSerializer]
[Alias("ui.graph-node")]
public sealed record GraphNodeState(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] string Kind = GraphNodeKinds.Leaf,
    [property: Id(3)] string? Cluster = null);

[GenerateSerializer]
[Alias("ui.graph-edge")]
public sealed record GraphEdgeState(
    [property: Id(0)] string Id,
    [property: Id(1)] string SourceId,
    [property: Id(2)] string TargetId,
    [property: Id(3)] bool Dotted = false);

// The client renders exactly one hub at the graph's centre; every other node
// sits on the shell. Kept as strings so the wire shape stays a plain record.
public static class GraphNodeKinds
{
    public const string Hub = "hub";
    public const string Leaf = "leaf";
}
