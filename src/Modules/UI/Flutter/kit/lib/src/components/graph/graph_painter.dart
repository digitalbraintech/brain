import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../theme/kit_theme.dart';
import 'graph_geometry.dart';
import 'graph_models.dart';

final class GraphPainter extends CustomPainter {
  const GraphPainter({
    required this.nodes,
    required this.edges,
    required this.pulse,
    required this.pulseValue,
    this.highlightEdgeId,
  });

  final List<ProjectedGraphNode> nodes;
  final List<ProjectedGraphEdge> edges;
  final GraphPulse? pulse;
  final double pulseValue;
  final String? highlightEdgeId;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width * 0.5, size.height * 0.51);
    _paintHull(canvas, size, center);
    _paintEdges(canvas, center);
    _paintPulse(canvas, center, size);
    _paintNodes(canvas);
  }

  void _paintHull(Canvas canvas, Size size, Offset center) {
    final hull = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = KitPalette.lineStrong.withValues(alpha: 0.55);
    final radius = math.min(size.width, size.height) * 0.34;
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 2.15,
        height: radius * 1.55,
      ),
      hull,
    );
    canvas.drawOval(
      Rect.fromCenter(
        center: center,
        width: radius * 1.35,
        height: radius * 2.05,
      ),
      hull..color = KitPalette.line.withValues(alpha: 0.5),
    );
  }

  void _paintEdges(Canvas canvas, Offset center) {
    for (final edge in edges) {
      final control = graphEdgeControl(edge, center);
      final depthAlpha = (0.45 + (edge.depth + 1) * 0.2).clamp(0.3, 0.9);
      final recent = edge.edge.id == highlightEdgeId;
      final color = recent ? KitPalette.signal : KitPalette.line;

      final path = Path()
        ..moveTo(edge.from.center.dx, edge.from.center.dy)
        ..quadraticBezierTo(
          control.dx,
          control.dy,
          edge.to.center.dx,
          edge.to.center.dy,
        );
      canvas.drawPath(
        edge.edge.dotted ? _dashed(path) : path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = recent ? 2.4 : 1.4
          ..color = color.withValues(alpha: recent ? 0.95 : depthAlpha),
      );

      final tip = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        0.86,
      );
      final tail = graphQuadraticPoint(
        edge.from.center,
        control,
        edge.to.center,
        0.78,
      );
      final direction = (tip - tail).direction;
      const arrow = 6.0;
      final head = Path()
        ..moveTo(tip.dx, tip.dy)
        ..lineTo(
          tip.dx - arrow * math.cos(direction - 0.45),
          tip.dy - arrow * math.sin(direction - 0.45),
        )
        ..lineTo(
          tip.dx - arrow * math.cos(direction + 0.45),
          tip.dy - arrow * math.sin(direction + 0.45),
        )
        ..close();
      canvas.drawPath(
        head,
        Paint()..color = color.withValues(alpha: recent ? 0.95 : depthAlpha),
      );

      if (edge.edge.decorated) {
        final bead = graphQuadraticPoint(
          edge.from.center,
          control,
          edge.to.center,
          0.5,
        );
        canvas.drawCircle(
          bead,
          3.2,
          Paint()..color = KitPalette.owner.withValues(alpha: depthAlpha),
        );
        canvas.drawCircle(
          bead,
          3.2,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 1
            ..color = KitPalette.textPrimary.withValues(alpha: 0.4),
        );
      }
    }
  }

  static Path _dashed(Path path, {double dash = 5, double gap = 4}) {
    final dashed = Path();
    for (final metric in path.computeMetrics()) {
      var distance = 0.0;
      while (distance < metric.length) {
        final end = math.min(distance + dash, metric.length);
        dashed.addPath(metric.extractPath(distance, end), Offset.zero);
        distance = end + gap;
      }
    }
    return dashed;
  }

  void _paintPulse(Canvas canvas, Offset center, Size size) {
    final active = pulse;
    if (active == null) {
      return;
    }

    final target = nodes
        .where((node) => node.node.id == active.toId)
        .firstOrNull;
    if (target == null) {
      return;
    }
    final source = nodes
        .where((node) => node.node.id == active.fromId)
        .firstOrNull;

    final radius = math.min(size.width, size.height) * 0.34;
    final wave = math.sin(pulseValue * math.pi).abs();
    if (source != null && source.node.id != target.node.id) {
      final path = Path()
        ..moveTo(source.center.dx, source.center.dy)
        ..quadraticBezierTo(
          center.dx,
          center.dy - radius * 0.45,
          target.center.dx,
          target.center.dy,
        );
      canvas.drawPath(
        path,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2.4
          ..color = KitPalette.signal.withValues(alpha: 0.25 + wave * 0.7),
      );
    }
    canvas.drawCircle(
      target.center,
      target.radius + 8 + wave * 16,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..color = KitPalette.signal.withValues(alpha: 0.7 - wave * 0.3),
    );
  }

  void _paintNodes(Canvas canvas) {
    for (final projected in nodes) {
      final node = projected.node;
      final color = switch (node.kind) {
        GraphNodeKind.hub => KitPalette.signal,
        GraphNodeKind.entity => KitPalette.success,
        GraphNodeKind.module => KitPalette.signal,
        GraphNodeKind.leaf => KitPalette.owner,
      };
      final dimFactor = node.dimmed ? 0.45 : 1.0;
      final depthAlpha =
          ((0.5 + (projected.depth + 1) * 0.22).clamp(0.35, 1.0)) * dimFactor;
      canvas.drawCircle(
        projected.center,
        projected.radius * 1.8,
        Paint()
          ..color = color.withValues(alpha: 0.06 * depthAlpha)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
      );
      _paintNodeBody(canvas, projected, color, depthAlpha);

      final text = TextPainter(
        text: TextSpan(
          text: node.label,
          style: KitType.meta.copyWith(
            color: KitPalette.textPrimary.withValues(alpha: depthAlpha),
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 120);
      text.paint(
        canvas,
        projected.center + Offset(-text.width / 2, projected.radius + 7),
      );
    }
  }

  static void _paintNodeBody(
    Canvas canvas,
    ProjectedGraphNode projected,
    Color color,
    double depthAlpha,
  ) {
    final fill = Paint()..color = color.withValues(alpha: depthAlpha);
    final stroke = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = KitPalette.textPrimary.withValues(alpha: 0.3);
    final r = projected.radius;
    final c = projected.center;

    switch (projected.node.kind) {
      case GraphNodeKind.entity:
        final rect = Rect.fromCenter(center: c, width: r * 2, height: r * 2);
        canvas.drawRRect(RRect.fromRectXY(rect, 3, 3), fill);
        canvas.drawRRect(RRect.fromRectXY(rect, 3, 3), stroke);
      case GraphNodeKind.module:
        final diamond = Path()
          ..moveTo(c.dx, c.dy - r)
          ..lineTo(c.dx + r, c.dy)
          ..lineTo(c.dx, c.dy + r)
          ..lineTo(c.dx - r, c.dy)
          ..close();
        canvas.drawPath(diamond, fill);
        canvas.drawPath(diamond, stroke);
      case GraphNodeKind.hub:
      case GraphNodeKind.leaf:
        canvas.drawCircle(c, r, fill);
        canvas.drawCircle(c, r, stroke);
    }
  }

  @override
  bool shouldRepaint(covariant GraphPainter oldDelegate) =>
      oldDelegate.nodes != nodes ||
      oldDelegate.edges != edges ||
      oldDelegate.pulse != pulse ||
      oldDelegate.pulseValue != pulseValue ||
      oldDelegate.highlightEdgeId != highlightEdgeId;
}
