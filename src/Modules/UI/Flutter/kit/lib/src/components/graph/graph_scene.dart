import 'package:flutter/widgets.dart';

import 'graph_camera.dart';
import 'graph_models.dart';

/// The rendering seam for the 3D graph view.
///
/// The view talks only to this, so tests can substitute a fake and never need
/// a GL context. `ThreeGraphScene` is the real implementation.
abstract interface class GraphScene {
  /// Builds meshes for the graph. Safe to call before the renderer is ready --
  /// implementations buffer until they can draw.
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  );

  /// Positions the renderer's camera from a pose.
  void applyCamera(GraphCameraState state);

  /// Node id under [local] in widget-local pixels, or null for empty space.
  String? pick(Offset local);

  /// Screen position of a node, or null when it is behind the near plane.
  Offset? project(String nodeId);

  /// The renderer's own widget.
  Widget build(BuildContext context);

  void dispose();
}

typedef GraphSceneFactory = GraphScene Function();

/// Optional signal animation supported by a renderer.
abstract interface class AnimatedGraphScene {
  void setPulse(GraphPulse? pulse);
  void advance(double seconds);
}
