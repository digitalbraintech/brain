import 'package:flutter/material.dart';

import '../../theme/kit_theme.dart';
import 'graph_models.dart';
import 'kit_graph_controller.dart';

/// Visible navigation for [KitGraphView]: where you are, how you got there,
/// and where you can go next.
///
/// Pure Flutter -- no 3D code -- so the navigation behaviour is fully
/// widget-testable while the renderer stays a thin shell.
final class KitGraphNavigator extends StatelessWidget {
  const KitGraphNavigator({super.key, required this.controller});

  final KitGraphController controller;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: controller,
      builder: (context, _) {
        final selected = controller.selected;
        return DecoratedBox(
          decoration: const BoxDecoration(
            color: KitPalette.surfaceRaised,
            border: Border(top: BorderSide(color: KitPalette.line)),
          ),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(6, 4, 10, 8),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    IconButton(
                      key: const Key('kit_graph_nav_back'),
                      icon: const Icon(Icons.arrow_back, size: 18),
                      color: KitPalette.textMuted,
                      tooltip: 'Back',
                      onPressed: controller.canGoBack ? controller.back : null,
                    ),
                    IconButton(
                      key: const Key('kit_graph_nav_forward'),
                      icon: const Icon(Icons.arrow_forward, size: 18),
                      color: KitPalette.textMuted,
                      tooltip: 'Forward',
                      onPressed: controller.canGoForward
                          ? controller.forward
                          : null,
                    ),
                    Expanded(child: _crumbs(controller.breadcrumb)),
                  ],
                ),
                if (selected != null) ...[
                  const SizedBox(height: 4),
                  _neighbours(controller.neighbours(selected)),
                ],
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _crumbs(List<GraphNode> path) {
    if (path.isEmpty) {
      return const Text(
        'Tap a node to explore the graph',
        key: Key('kit_graph_nav_empty'),
        style: KitType.bodyMuted,
      );
    }
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (var i = 0; i < path.length; i++) ...[
            if (i > 0)
              const Padding(
                padding: EdgeInsets.symmetric(horizontal: 2),
                child: Text('/', style: KitType.meta),
              ),
            TextButton(
              key: Key('kit_graph_crumb_${path[i].id}'),
              style: TextButton.styleFrom(
                minimumSize: Size.zero,
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
              onPressed: () => controller.focus(path[i].id),
              child: Text(
                path[i].label,
                style: i == path.length - 1
                    ? KitType.metaStrong
                    : KitType.bodyMuted,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _neighbours(GraphNeighbours neighbours) {
    final chips = <Widget>[
      for (final node in neighbours.incoming) _chip(node, incoming: true),
      for (final node in neighbours.outgoing) _chip(node, incoming: false),
    ];
    if (chips.isEmpty) {
      return const Padding(
        padding: EdgeInsets.only(left: 8),
        child: Text('No connections', style: KitType.meta),
      );
    }
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(children: chips),
    );
  }

  Widget _chip(GraphNode node, {required bool incoming}) {
    return Padding(
      padding: const EdgeInsets.only(left: 6),
      child: ActionChip(
        key: Key('kit_graph_neighbour_${node.id}'),
        visualDensity: VisualDensity.compact,
        materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
        avatar: Icon(
          incoming ? Icons.subdirectory_arrow_left : Icons.arrow_outward,
          size: 13,
          color: KitPalette.textFaint,
        ),
        label: Text(node.label, style: KitType.meta),
        backgroundColor: KitPalette.surface,
        side: const BorderSide(color: KitPalette.line),
        onPressed: () => controller.focus(node.id),
      ),
    );
  }
}
