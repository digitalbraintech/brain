import 'dart:math' as math;

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../../digitalbrain_ui_kit.dart';

/// A growing whiteboard of real neurons. Existing positions survive new traffic
/// and new participants; dragging changes presentation only.
final class LumenBrainGraph extends StatefulWidget {
  const LumenBrainGraph({
    super.key,
    required this.snapshot,
    required this.onNeuron,
    required this.onSynapse,
    this.onActivity,
    this.selectedId,
    this.stale = false,
    this.activeNodes = const {},
    this.activeEdges = const {},
  });
  final BrainSnapshot snapshot;
  final ValueChanged<BrainNeuron> onNeuron;
  final ValueChanged<BrainSynapse> onSynapse;
  final ValueChanged<BrainActivity>? onActivity;
  final String? selectedId;
  final bool stale;
  final Set<String> activeNodes, activeEdges;
  @override
  State<LumenBrainGraph> createState() => _LumenBrainGraphState();
}

final class _LumenBrainGraphState extends State<LumenBrainGraph> {
  final _transform = TransformationController();
  final _positions = <String, Offset>{};
  bool _viewInitialized = false;

  void _fit(Size viewport, Size canvas) {
    final scale = math
        .min(viewport.width / canvas.width, viewport.height / canvas.height)
        .clamp(0.15, 1.0);
    _transform.value = Matrix4.identity()
      ..translateByDouble(
        (viewport.width - canvas.width * scale) / 2,
        (viewport.height - canvas.height * scale) / 2,
        0,
        1,
      )
      ..scaleByDouble(scale, scale, 1, 1);
  }

  @override
  void dispose() {
    _transform.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final assistantWorking =
        !widget.stale &&
        widget.snapshot.nodes.any(
          (node) =>
              brainNeuronIcon(node) == NeuronIconKind.assistant &&
              (node.status == 'Running' || node.status == 'Active'),
        );
    final ordered = [...widget.snapshot.nodes]
      ..sort(
        (a, b) => brainNeuronIcon(a) == NeuronIconKind.assistant
            ? -1
            : brainNeuronIcon(b) == NeuronIconKind.assistant
            ? 1
            : a.id.compareTo(b.id),
      );
    for (final node in ordered) {
      _positions.putIfAbsent(node.id, () {
        if (brainNeuronIcon(node) == NeuronIconKind.assistant) {
          return const Offset(450, 300);
        }
        final index = _positions.length - 1;
        if (index >= 8) {
          return Offset(
            120 + ((index - 8) % 5) * 180,
            650 + ((index - 8) ~/ 5) * 160,
          );
        }
        final ring = 1 + index ~/ 8;
        final angle = (index % 8) * math.pi / 4;
        return Offset(
          450 + math.cos(angle) * ring * 180,
          300 + math.sin(angle) * ring * 150,
        );
      });
    }
    final positions = {
      for (final node in ordered) node.id: _positions[node.id]!,
    };
    final size = Size(
      math.max(
        900,
        positions.values.fold(
          0.0,
          (value, point) => math.max(value, point.dx + 100),
        ),
      ),
      math.max(
        600,
        positions.values.fold(
          0.0,
          (value, point) => math.max(value, point.dy + 100),
        ),
      ),
    );
    final routes = _synapseRoutes(positions, widget.snapshot.synapses);
    final delegations = widget.stale
        ? <BrainActivity>[]
        : widget.snapshot.activeDelegations
              .where(
                (event) =>
                    positions.containsKey(event.neuronId) &&
                    positions.containsKey(event.targetId),
              )
              .toList();
    return LayoutBuilder(
      builder: (context, constraints) {
        if (!_viewInitialized) {
          _fit(constraints.biggest, size);
          _viewInitialized = true;
        }
        return Stack(
          children: [
            Positioned.fill(
              child: InteractiveViewer(
                key: const Key('lumen_graph_canvas'),
                transformationController: _transform,
                constrained: false,
                alignment: Alignment.topLeft,
                minScale: 0.15,
                maxScale: 3,
                boundaryMargin: const EdgeInsets.all(1000),
                child: SizedBox(
                  width: size.width,
                  height: size.height,
                  child: Stack(
                    children: [
                      Positioned.fill(
                        child: CustomPaint(
                          painter: _SynapsePainter(
                            routes,
                            widget.activeEdges,
                            widget.selectedId,
                          ),
                        ),
                      ),
                      Positioned.fill(
                        child: IgnorePointer(
                          child: CustomPaint(
                            painter: _DelegationPainter(positions, delegations),
                          ),
                        ),
                      ),
                      for (final event in delegations)
                        Positioned(
                          left:
                              (positions[event.neuronId]!.dx +
                                      positions[event.targetId]!.dx) /
                                  2 -
                              70,
                          top:
                              (positions[event.neuronId]!.dy +
                                      positions[event.targetId]!.dy) /
                                  2 -
                              100,
                          child: LumenActionButton(
                            key: ValueKey('delegation_${event.operationId}'),
                            label: 'Request running',
                            onPressed: widget.onActivity == null
                                ? null
                                : () => widget.onActivity!(event),
                          ),
                        ),
                      for (final route in routes)
                        Positioned(
                          left: route.controlPosition.dx - 22,
                          top: route.controlPosition.dy - 22,
                          child: SizedBox.square(
                            dimension: 44,
                            child: LumenIconButton(
                              key: ValueKey('synapse_${route.edge.id}'),
                              label:
                                  'Inspect ${route.edge.kind} synapse ${route.edge.signalType}, from ${route.edge.sourceId} to ${route.edge.targetId}',
                              selected: widget.selectedId == route.edge.id,
                              onPressed: () => widget.onSynapse(route.edge),
                              icon: const Icon(
                                Icons.arrow_outward_rounded,
                                size: 16,
                              ),
                            ),
                          ),
                        ),
                      for (final node in widget.snapshot.nodes)
                        Positioned(
                          left: positions[node.id]!.dx - 58,
                          top: positions[node.id]!.dy - 42,
                          child: GestureDetector(
                            onPanUpdate: (details) => setState(() {
                              final next = _positions[node.id]! + details.delta;
                              _positions[node.id] = Offset(
                                math.max(65, next.dx),
                                math.max(50, next.dy),
                              );
                            }),
                            child: _NeuronTile(
                              node: node,
                              selected: widget.selectedId == node.id,
                              active:
                                  !widget.stale &&
                                  (node.status == 'Running' ||
                                      widget.activeNodes.contains(node.id) ||
                                      (brainNeuronIcon(node) ==
                                              NeuronIconKind.assistant &&
                                          assistantWorking)),
                              stale: widget.stale,
                              onTap: () => widget.onNeuron(node),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
            Positioned(
              right: 12,
              bottom: 8,
              child: IconButton.filledTonal(
                tooltip: 'Reset graph view',
                onPressed: () => _fit(constraints.biggest, size),
                icon: const Icon(Icons.center_focus_strong_outlined, size: 18),
              ),
            ),
          ],
        );
      },
    );
  }
}

NeuronIconKind brainNeuronIcon(BrainNeuron node) {
  // Older snapshots lack presentation keys. Preserve exact built-in identities;
  // provider icons always come from their module-owned descriptor.
  final key =
      node.iconKey ??
      switch (node.type.toLowerCase()) {
        'assistant' => 'assistant',
        'chat' => 'conversation',
        'chat-turn-worker' || 'execution' => 'execution',
        'sessionneuron' || 'memory' => 'memory',
        'behaviors' => 'repository',
        'timer' => 'clock',
        _ => null,
      };
  return NeuronIconKind.fromKey(key);
}

final class _NeuronTile extends StatelessWidget {
  const _NeuronTile({
    required this.node,
    required this.selected,
    required this.active,
    required this.stale,
    required this.onTap,
  });
  final BrainNeuron node;
  final bool selected, active, stale;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final assistant = brainNeuronIcon(node) == NeuronIconKind.assistant;
    final failed = node.status == 'Failed';
    return Semantics(
      button: true,
      label:
          '${node.label}, ${node.module}, ${assistant && active ? 'Working' : node.status}',
      child: InkWell(
        key: ValueKey('neuron_${node.id}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(18),
        child: SizedBox(
          width: 116,
          child: Column(
            children: [
              AnimatedContainer(
                duration: MediaQuery.disableAnimationsOf(context)
                    ? Duration.zero
                    : const Duration(milliseconds: 250),
                width: 64,
                height: 64,
                decoration: BoxDecoration(
                  color: Colors.white,
                  borderRadius: BorderRadius.circular(19),
                  border: Border.all(
                    width: selected || active ? 2 : 1,
                    color: failed && !stale
                        ? LumenPalette.error
                        : selected || active
                        ? const Color(0xff397b63)
                        : const Color(0xffdce3d9),
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xff4c6b4b)
                          .withValues(alpha: active ? .18 : .06),
                      blurRadius: active ? 24 : 12,
                      offset: const Offset(0, 5),
                    ),
                  ],
                ),
                child: Stack(
                  alignment: Alignment.center,
                  children: [
                    if (!assistant && node.status == 'Running' && !stale)
                      Positioned.fill(
                        child: Padding(
                          padding: const EdgeInsets.all(3),
                          child: CircularProgressIndicator(
                            strokeWidth: 1.5,
                            value: MediaQuery.disableAnimationsOf(context)
                                ? 1
                                : null,
                            color: const Color(0xff397b63),
                            semanticsLabel: '${node.label} working',
                          ),
                        ),
                      ),
                    assistant
                        ? InoPresence(
                            size: 58,
                            state: stale
                                ? InoPresenceState.disconnected
                                : active || node.status == 'Running'
                                ? InoPresenceState.working
                                : node.status == 'Failed'
                                ? InoPresenceState.attention
                                : InoPresenceState.idle,
                          )
                        : NeuronIcon(kind: brainNeuronIcon(node), size: 32),
                  ],
                ),
              ),
              const SizedBox(height: 8),
              Text(
                assistant ? 'Ino' : node.label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontSize: 12,
                  color: Color(0xff263934),
                  fontWeight: FontWeight.w600,
                ),
              ),
              Text(
                failed && !stale
                    ? 'Needs attention'
                    : node.status == 'Running' && !stale
                    ? 'Working'
                    : active
                    ? (assistant ? 'Working' : 'Just active')
                    : node.name,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 9, color: Color(0xff75847b)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A request trace is temporary activity, never a synthetic BrainSynapse.
final class _DelegationPainter extends CustomPainter {
  _DelegationPainter(this.positions, this.events);
  final Map<String, Offset> positions;
  final List<BrainActivity> events;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = const Color(0xff7455dd)
      ..strokeWidth = 2
      ..style = PaintingStyle.stroke;
    for (final event in events) {
      final from = positions[event.neuronId]!;
      final to = positions[event.targetId]!;
      final path = Path()..moveTo(from.dx, from.dy);
      path.quadraticBezierTo(
        (from.dx + to.dx) / 2,
        (from.dy + to.dy) / 2 - 60,
        to.dx,
        to.dy,
      );
      final metrics = path.computeMetrics();
      for (final metric in metrics) {
        for (var distance = 0.0; distance < metric.length; distance += 8) {
          canvas.drawPath(
            metric.extractPath(distance, math.min(metric.length, distance + 2)),
            paint,
          );
        }
      }
    }
  }

  @override
  bool shouldRepaint(covariant _DelegationPainter oldDelegate) => true;
}

final class _SynapseRoute {
  const _SynapseRoute(this.edge, this.path, this.controlPosition);
  final BrainSynapse edge;
  final Path path;
  final Offset controlPosition;
}

// The drawing and its inspector button share one route. Group both directions
// together so parallel subscriptions remain individually reachable.
List<_SynapseRoute> _synapseRoutes(
  Map<String, Offset> positions,
  List<BrainSynapse> edges,
) {
  final groups = <(String, String), List<BrainSynapse>>{};
  for (final edge in edges) {
    if (!positions.containsKey(edge.sourceId) ||
        !positions.containsKey(edge.targetId)) {
      continue;
    }
    final pair = edge.sourceId.compareTo(edge.targetId) <= 0
        ? (edge.sourceId, edge.targetId)
        : (edge.targetId, edge.sourceId);
    groups.putIfAbsent(pair, () => []).add(edge);
  }
  final routes = <_SynapseRoute>[];
  for (final group in groups.entries) {
    final siblings = group.value..sort((a, b) => a.id.compareTo(b.id));
    final canonical = positions[group.key.$2]! - positions[group.key.$1]!;
    for (var i = 0; i < siblings.length; i++) {
      final edge = siblings[i];
      final from = positions[edge.sourceId]!, to = positions[edge.targetId]!;
      final delta = to - from;
      final path = Path();
      late final Offset controlPosition;
      if (delta.distance < 1) {
        final reach = 86.0 + i * 62;
        path.moveTo(from.dx + 30, from.dy);
        path.cubicTo(
          from.dx + reach,
          from.dy - reach,
          from.dx - reach,
          from.dy - reach,
          from.dx - 30,
          from.dy,
        );
        controlPosition = Offset(from.dx, from.dy - reach * .75);
      } else {
        final a = from + delta / delta.distance * 36;
        final b = to - delta / delta.distance * 36;
        final normal = Offset(-canonical.dy, canonical.dx) / canonical.distance;
        final bend = (i - (siblings.length - 1) / 2) * 52;
        final midpoint = (a + b) / 2;
        final control = midpoint + normal * bend * 2;
        controlPosition = midpoint + normal * bend;
        path.moveTo(a.dx, a.dy);
        path.quadraticBezierTo(control.dx, control.dy, b.dx, b.dy);
      }
      routes.add(_SynapseRoute(edge, path, controlPosition));
    }
  }
  return routes;
}

final class _SynapsePainter extends CustomPainter {
  _SynapsePainter(this.routes, this.active, this.selected);
  final List<_SynapseRoute> routes;
  final Set<String> active;
  final String? selected;
  @override
  void paint(Canvas canvas, Size size) {
    for (final route in routes) {
      final edge = route.edge;
      final emphasis = active.contains(edge.id) || edge.id == selected;
      final paint = Paint()
        ..color = emphasis ? const Color(0xff397b63) : const Color(0xffb3c6b5)
        ..strokeWidth = emphasis ? 2.5 : 1.5
        ..style = PaintingStyle.stroke;
      final metric = route.path.computeMetrics().first;
      if (edge.kind == 'Learned' || edge.kind == 'Observed') {
        for (double distance = 0; distance < metric.length; distance += 12) {
          canvas.drawPath(
            metric.extractPath(distance, math.min(metric.length, distance + 6)),
            paint,
          );
        }
      } else {
        canvas.drawPath(route.path, paint);
      }
      final tangent = metric.getTangentForOffset(metric.length)!;
      final b = tangent.position;
      final angle = math.atan2(tangent.vector.dy, tangent.vector.dx);
      final arrow = Path()
        ..moveTo(b.dx, b.dy)
        ..lineTo(
          b.dx - 9 * math.cos(angle - .5),
          b.dy - 9 * math.sin(angle - .5),
        )
        ..moveTo(b.dx, b.dy)
        ..lineTo(
          b.dx - 9 * math.cos(angle + .5),
          b.dy - 9 * math.sin(angle + .5),
        );
      canvas.drawPath(arrow, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _SynapsePainter oldDelegate) => true;
}
