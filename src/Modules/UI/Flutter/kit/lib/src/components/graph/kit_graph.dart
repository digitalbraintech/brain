import 'package:flutter/material.dart';

import 'graph_geometry.dart';
import 'graph_models.dart';
import 'graph_painter.dart';

/// Product graph control: depth-projected nodes with directed edges.
/// Same widget for surface galleries, the Brain view, and future diagrams.
final class KitGraph extends StatefulWidget {
  const KitGraph({
    super.key,
    required this.nodes,
    required this.edges,
    this.pulse,
    this.highlightEdgeId,
    this.onNodeTap,
    this.onEdgeTap,
    this.semanticsLabel = 'Interactive three-dimensional graph. Drag to rotate; tap a node or edge to inspect it.',
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final GraphPulse? pulse;
  final String? highlightEdgeId;
  final ValueChanged<GraphNode>? onNodeTap;
  final ValueChanged<GraphEdge>? onEdgeTap;
  final String semanticsLabel;

  @override
  State<KitGraph> createState() => _KitGraphState();
}

final class _KitGraphState extends State<KitGraph>
    with SingleTickerProviderStateMixin {
  late final AnimationController _pulse;
  double _rotationX = -0.18;
  double _rotationY = 0.42;

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1100),
    );
    if (widget.pulse != null) {
      _pulse.forward();
    }
  }

  @override
  void didUpdateWidget(covariant KitGraph oldWidget) {
    super.didUpdateWidget(oldWidget);
    final pulseChanged = widget.pulse?.signature != oldWidget.pulse?.signature;
    if (widget.pulse == null) {
      if (pulseChanged || oldWidget.pulse != null) {
        _pulse.reset();
      }
    } else if (pulseChanged || oldWidget.pulse == null) {
      _pulse.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  void _rotate(DragUpdateDetails details) {
    setState(() {
      _rotationY += details.delta.dx * 0.008;
      _rotationX = (_rotationX + details.delta.dy * 0.008).clamp(-1.0, 1.0);
    });
  }

  @override
  Widget build(BuildContext context) {
    final disableAnimations = MediaQuery.disableAnimationsOf(context);

    return Semantics(
      key: const Key('kit_graph'),
      label: widget.semanticsLabel,
      image: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final size = Size(constraints.maxWidth, constraints.maxHeight);
          return AnimatedBuilder(
            animation: _pulse,
            builder: (context, _) {
              final projected = projectGraphNodes(
                widget.nodes,
                size,
                _rotationX,
                _rotationY,
              );
              final projectedEdges = projectGraphEdges(widget.edges, projected);
              final canvasCenter = Offset(size.width * 0.5, size.height * 0.51);
              final pulseValue = disableAnimations ? 1.0 : _pulse.value;

              return GestureDetector(
                behavior: HitTestBehavior.opaque,
                onPanUpdate: _rotate,
                onTapUp: (details) {
                  final node = hitTestGraphNodes(
                    projected,
                    details.localPosition,
                  );
                  if (node != null) {
                    widget.onNodeTap?.call(node.node);
                    return;
                  }
                  final edge = hitTestGraphEdges(
                    projectedEdges,
                    details.localPosition,
                    canvasCenter,
                  );
                  if (edge != null) {
                    widget.onEdgeTap?.call(edge.edge);
                  }
                },
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    CustomPaint(
                      painter: GraphPainter(
                        nodes: projected,
                        edges: projectedEdges,
                        pulse: widget.pulse,
                        pulseValue: pulseValue,
                        highlightEdgeId: widget.highlightEdgeId,
                      ),
                    ),
                    for (final edge in projectedEdges)
                      IgnorePointer(key: Key('graph_edge_${edge.edge.id}')),
                  ],
                ),
              );
            },
          );
        },
      ),
    );
  }
}
