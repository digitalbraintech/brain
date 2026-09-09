import 'dart:math' as math;

import 'graph_models.dart';

/// Assigns every node a world coordinate.
///
/// A node's own [GraphNode.position] wins. Hubs sit at the origin. Everything
/// else lands on a Fibonacci sphere, ordered by a stable hash of the node id --
/// so a graph keeps the same shape across reloads and regardless of the order
/// the nodes arrive in.
Map<String, GraphPoint> layoutGraph(
  List<GraphNode> nodes, {
  double radius = 1.0,
}) {
  final layout = <String, GraphPoint>{};
  final shell = <GraphNode>[];

  for (final node in nodes) {
    if (node.position != null) {
      layout[node.id] = node.position!;
    } else if (node.kind == GraphNodeKind.hub) {
      layout[node.id] = const GraphPoint(0, 0, 0);
    } else {
      shell.add(node);
    }
  }

  shell.sort((a, b) {
    final byHash = _stableHash(a.id).compareTo(_stableHash(b.id));
    return byHash != 0 ? byHash : a.id.compareTo(b.id);
  });

  // Golden-angle increment: pi * (3 - sqrt(5)).
  final golden = math.pi * (3 - math.sqrt(5));
  for (var i = 0; i < shell.length; i++) {
    final y = shell.length == 1 ? 0.0 : 1 - (i / (shell.length - 1)) * 2;
    final ring = math.sqrt(math.max(0, 1 - y * y));
    final theta = golden * i;
    layout[shell[i].id] = GraphPoint(
      math.cos(theta) * ring * radius,
      y * radius,
      math.sin(theta) * ring * radius,
    );
  }

  return layout;
}

/// FNV-1a. Deterministic across runs and platforms, unlike [String.hashCode].
int _stableHash(String value) {
  var hash = 0x811c9dc5;
  for (final unit in value.codeUnits) {
    hash ^= unit;
    hash = (hash * 0x01000193) & 0xFFFFFFFF;
  }
  return hash;
}
