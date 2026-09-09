import 'dart:math' as math;

import 'package:flutter/widgets.dart';

import 'graph_models.dart';

final class ProjectedGraphNode {
  const ProjectedGraphNode({
    required this.node,
    required this.center,
    required this.radius,
    required this.depth,
  });

  final GraphNode node;
  final Offset center;
  final double radius;
  final double depth;
}

final class ProjectedGraphEdge {
  const ProjectedGraphEdge({
    required this.edge,
    required this.from,
    required this.to,
  });

  final GraphEdge edge;
  final ProjectedGraphNode from;
  final ProjectedGraphNode to;

  double get depth => math.min(from.depth, to.depth);
}

List<ProjectedGraphNode> projectGraphNodes(
  List<GraphNode> nodes,
  Size size,
  double rotationX,
  double rotationY,
) {
  final hubs = nodes.where((node) => node.kind == GraphNodeKind.hub).toList();
  final leaves = nodes.where((node) => node.kind != GraphNodeKind.hub).toList();

  final placed = <({GraphNode node, double x, double y, double z})>[
    for (var index = 0; index < hubs.length; index++)
      (
        node: hubs[index],
        x: _spherePosition(index, hubs.length, 0.88, 0).x,
        y: _spherePosition(index, hubs.length, 0.88, 0).y,
        z: _spherePosition(index, hubs.length, 0.88, 0).z,
      ),
    for (var index = 0; index < leaves.length; index++)
      (
        node: leaves[index],
        x: _spherePosition(index, leaves.length, 0.62, 1.3).x,
        y: _spherePosition(index, leaves.length, 0.62, 1.3).y,
        z: _spherePosition(index, leaves.length, 0.62, 1.3).z,
      ),
  ];

  final base = math.min(size.width, size.height) * 0.36;
  final center = Offset(size.width * 0.5, size.height * 0.51);
  final cosY = math.cos(rotationY);
  final sinY = math.sin(rotationY);
  final cosX = math.cos(rotationX);
  final sinX = math.sin(rotationX);

  final projected = <ProjectedGraphNode>[];
  for (final entry in placed) {
    final xY = entry.x * cosY + entry.z * sinY;
    final zY = -entry.x * sinY + entry.z * cosY;
    final yX = entry.y * cosX - zY * sinX;
    final zX = entry.y * sinX + zY * cosX;
    final perspective = 1.0 / (1.85 - zX * 0.36);
    final radius =
        (entry.node.kind == GraphNodeKind.hub ? 10.0 : 6.0) *
        (0.72 + perspective);

    projected.add(
      ProjectedGraphNode(
        node: entry.node,
        center: Offset(
          center.dx + xY * base * perspective,
          center.dy + yX * base * perspective,
        ),
        radius: radius,
        depth: zX,
      ),
    );
  }

  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

List<ProjectedGraphEdge> projectGraphEdges(
  List<GraphEdge> edges,
  List<ProjectedGraphNode> nodes,
) {
  final byId = {for (final node in nodes) node.node.id: node};
  final projected = <ProjectedGraphEdge>[];
  for (final edge in edges) {
    final from = byId[edge.sourceId];
    final to = byId[edge.targetId];
    if (from == null || to == null || identical(from, to)) {
      continue;
    }
    projected.add(ProjectedGraphEdge(edge: edge, from: from, to: to));
  }
  projected.sort((a, b) => a.depth.compareTo(b.depth));
  return projected;
}

Offset graphEdgeControl(ProjectedGraphEdge edge, Offset canvasCenter) {
  final mid = Offset(
    (edge.from.center.dx + edge.to.center.dx) / 2,
    (edge.from.center.dy + edge.to.center.dy) / 2,
  );
  return Offset.lerp(mid, canvasCenter, 0.18)! - const Offset(0, 14);
}

Offset graphQuadraticPoint(Offset a, Offset control, Offset b, double t) {
  final u = 1 - t;
  return a * (u * u) + control * (2 * u * t) + b * (t * t);
}

ProjectedGraphNode? hitTestGraphNodes(
  List<ProjectedGraphNode> nodes,
  Offset position,
) {
  for (final node in nodes.reversed) {
    if ((node.center - position).distance <= node.radius + 8) {
      return node;
    }
  }
  return null;
}

ProjectedGraphEdge? hitTestGraphEdges(
  List<ProjectedGraphEdge> edges,
  Offset position,
  Offset canvasCenter,
) {
  for (final edge in edges.reversed) {
    final control = graphEdgeControl(edge, canvasCenter);
    for (var step = 1; step < 10; step++) {
      final sample = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        step / 10,
      );
      if ((sample - position).distance <= 9) {
        return edge;
      }
    }
  }
  return null;
}

({double x, double y, double z}) _spherePosition(
  int index,
  int count,
  double radius,
  double phase,
) {
  if (count <= 1) {
    return (x: 0, y: 0, z: radius);
  }
  final y = 1 - (2 * (index + 0.5) / count);
  final ring = math.sqrt(math.max(0, 1 - y * y));
  final theta = index * math.pi * (3 - math.sqrt(5)) + phase;
  return (
    x: math.cos(theta) * ring * radius,
    y: y * radius,
    z: math.sin(theta) * ring * radius,
  );
}
