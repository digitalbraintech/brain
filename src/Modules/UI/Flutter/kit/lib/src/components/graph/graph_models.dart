enum GraphNodeKind { hub, leaf, entity, module }

/// A world-space coordinate on the graph sphere.
final class GraphPoint {
  const GraphPoint(this.x, this.y, this.z);

  final double x;
  final double y;
  final double z;

  @override
  bool operator ==(Object other) =>
      other is GraphPoint && other.x == x && other.y == y && other.z == z;

  @override
  int get hashCode => Object.hash(x, y, z);

  @override
  String toString() => 'GraphPoint($x, $y, $z)';
}

final class GraphNode {
  const GraphNode({
    required this.id,
    required this.label,
    this.kind = GraphNodeKind.leaf,
    this.dimmed = false,
    this.cluster,
    this.position,
  });

  final String id;
  final String label;
  final GraphNodeKind kind;
  final bool dimmed;

  /// Grouping key -- nodes sharing a cluster are coloured and placed together.
  final String? cluster;

  /// Explicit world coordinate. When null, `layoutGraph` derives a stable one.
  final GraphPoint? position;
}

final class GraphEdge {
  const GraphEdge({
    required this.id,
    required this.sourceId,
    required this.targetId,
    this.decorated = false,
    this.dotted = false,
  });

  final String id;
  final String sourceId;
  final String targetId;
  final bool decorated;
  final bool dotted;
}

final class GraphPulse {
  const GraphPulse({
    required this.fromId,
    required this.toId,
    required this.signature,
  });

  final String fromId;
  final String toId;
  final String signature;

  bool get local => fromId == toId;
}
