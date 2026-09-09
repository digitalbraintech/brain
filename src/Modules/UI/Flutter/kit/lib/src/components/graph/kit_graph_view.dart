import 'dart:async';

import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import 'graph_scene.dart';
import 'graph_models.dart';
import 'kit_graph_controller.dart';
import 'three_graph_scene.dart';
import '../../theme/kit_theme.dart';

/// Interactive 3D graph. Drag to orbit, scroll to zoom, tap a node to focus it.
///
/// All navigation state lives in [controller]; this widget only renders and
/// reports picks. Pair it with `KitGraphNavigator` for visible navigation.
final class KitGraphView extends StatefulWidget {
  const KitGraphView({
    super.key,
    required this.controller,
    this.sceneFactory,
    this.pulse,
    this.showLabels = false,
    this.semanticsLabel = 'Interactive three-dimensional graph. Drag to orbit; tap a node to focus it.',
  });

  final KitGraphController controller;

  /// Substitutes the renderer. Tests pass a fake so no GL context is needed.
  final GraphSceneFactory? sceneFactory;
  final GraphPulse? pulse;
  final bool showLabels;

  final String semanticsLabel;

  @override
  State<KitGraphView> createState() => _KitGraphViewState();
}

class _KitGraphViewState extends State<KitGraphView>
    with SingleTickerProviderStateMixin {
  late final GraphScene _scene = (widget.sceneFactory ?? ThreeGraphScene.new)();

  /// Held as one instance: each `_scene.project` tear-off is a distinct
  /// closure, so comparing them with `identical` on dispose would never match.
  late final Offset? Function(String) _project = _scene.project;

  late final Ticker _ticker;
  Duration _last = Duration.zero;
  final _frames = ValueNotifier<int>(0);
  int _loadedRevision = -1;

  @override
  void initState() {
    super.initState();
    widget.controller.projector = _project;
    widget.controller.addListener(_onGraphChanged);
    _onGraphChanged();
    _setPulse();
    _ticker = createTicker(_onFrame)..start();
  }

  void _onGraphChanged() {
    if (_loadedRevision == widget.controller.graphRevision) return;
    _loadedRevision = widget.controller.graphRevision;
    unawaited(
      _scene.load(
        widget.controller.nodes,
        widget.controller.edges,
        widget.controller.layout,
      ),
    );
  }

  void _setPulse() {
    final scene = _scene;
    if (scene is AnimatedGraphScene) {
      (scene as AnimatedGraphScene).setPulse(widget.pulse);
    }
  }

  @override
  void didUpdateWidget(covariant KitGraphView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_onGraphChanged);
      if (identical(oldWidget.controller.projector, _project)) {
        oldWidget.controller.projector = null;
      }
      widget.controller.projector = _project;
      widget.controller.addListener(_onGraphChanged);
      _loadedRevision = -1;
      _onGraphChanged();
    }
    _setPulse();
  }

  void _onFrame(Duration elapsed) {
    final dt = (elapsed - _last).inMicroseconds / 1e6;
    _last = elapsed;
    if (dt <= 0) return;
    widget.controller.camera.tick(dt);
    _scene.applyCamera(widget.controller.camera.current);
    final scene = _scene;
    if (scene is AnimatedGraphScene) {
      (scene as AnimatedGraphScene).advance(dt);
    }
    if (widget.showLabels) _frames.value++;
  }

  @override
  void dispose() {
    _ticker.dispose();
    _frames.dispose();
    widget.controller.removeListener(_onGraphChanged);
    if (identical(widget.controller.projector, _project)) {
      widget.controller.projector = null;
    }
    _scene.dispose();
    super.dispose();
  }

  void _onTapUp(TapUpDetails details) {
    final hit = _scene.pick(details.localPosition);
    if (hit != null) {
      widget.controller.focus(hit);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: widget.semanticsLabel,
      image: true,
      child: Listener(
        onPointerSignal: (signal) {
          if (signal is PointerScrollEvent) {
            widget.controller.camera.zoomBy(
              signal.scrollDelta.dy > 0 ? 0.92 : 1.09,
            );
          }
        },
        child: GestureDetector(
          key: const Key('kit_graph_view'),
          behavior: HitTestBehavior.opaque,
          dragStartBehavior: DragStartBehavior.down,
          onTapUp: _onTapUp,
          onPanUpdate: (d) =>
              widget.controller.camera.orbitBy(d.delta.dx, d.delta.dy),
          child: Stack(
            fit: StackFit.expand,
            children: [
              // Renderers such as ThreeJS install their own scale recognizer.
              // The graph view owns input; letting both compete steals orbit
              // drags from this widget's pan recognizer.
              IgnorePointer(child: _scene.build(context)),
              if (widget.showLabels)
                IgnorePointer(
                  child: CustomPaint(
                    painter: _GraphLabels(
                      controller: widget.controller,
                      frames: _frames,
                      activeNode: widget.pulse?.toId,
                      direction: Directionality.of(context),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

final class _GraphLabels extends CustomPainter {
  _GraphLabels({
    required this.controller,
    required Listenable frames,
    required this.activeNode,
    required this.direction,
  }) : super(repaint: frames);

  final KitGraphController controller;
  final String? activeNode;
  final TextDirection direction;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.clipRect(Offset.zero & size);
    for (final node in controller.nodes) {
      final point = controller.projectToScreen(node.id);
      if (point == null) continue;
      final module = node.kind == GraphNodeKind.module;
      final painter = TextPainter(
        text: TextSpan(
          text: module ? node.label.toUpperCase() : node.label,
          style: (module ? KitType.metaStrong : KitType.meta).copyWith(
            fontSize: module ? 11 : 10,
            color: node.id == activeNode
                ? const Color(0xFFFFF0BD)
                : module
                ? KitPalette.textPrimary
                : KitPalette.textMuted,
            shadows: const [Shadow(color: Color(0xFF11141B), blurRadius: 5)],
          ),
        ),
        textDirection: direction,
      )..layout();
      final origin = module
          ? point + Offset(-painter.width / 2, -62)
          : point + const Offset(11, -6);
      painter.paint(canvas, origin);
      painter.dispose();
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _GraphLabels oldDelegate) =>
      controller != oldDelegate.controller ||
      activeNode != oldDelegate.activeNode;
}
