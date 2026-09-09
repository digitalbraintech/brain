import 'dart:collection';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart' show Offset;

import 'graph_camera.dart';
import 'graph_layout.dart';
import 'graph_models.dart';

/// Nodes one edge away from a node, split by direction.
typedef GraphNeighbours = ({
  List<GraphNode> incoming,
  List<GraphNode> outgoing,
});

/// Selection, camera and browser-style navigation over a graph.
///
/// Owns no rendering. The view installs [projector] so callers can place
/// Flutter overlays on top of 3D nodes.
final class KitGraphController extends ChangeNotifier {
  KitGraphController({
    required List<GraphNode> nodes,
    required List<GraphEdge> edges,
    GraphCamera? camera,
  }) : camera = camera ?? GraphCamera() {
    _ingest(nodes, edges);
  }

  final GraphCamera camera;

  List<GraphNode> _nodes = const [];
  List<GraphEdge> _edges = const [];
  Map<String, GraphPoint> _layout = const {};
  final Map<String, GraphNode> _byId = {};
  int _graphRevision = 0;

  final List<String> _history = [];
  int _cursor = -1;

  /// Installed by the view. Maps a node id to its current screen position.
  Offset? Function(String nodeId)? projector;

  List<GraphNode> get nodes => UnmodifiableListView(_nodes);
  List<GraphEdge> get edges => UnmodifiableListView(_edges);
  Map<String, GraphPoint> get layout => UnmodifiableMapView(_layout);
  int get graphRevision => _graphRevision;

  String? get selected => _cursor < 0 ? null : _history[_cursor];
  bool get canGoBack => _cursor > 0;
  bool get canGoForward => _cursor >= 0 && _cursor < _history.length - 1;

  void setGraph({
    required List<GraphNode> nodes,
    required List<GraphEdge> edges,
  }) {
    _ingest(nodes, edges);
    // Drop history entries for nodes the new graph no longer has.
    _history.removeWhere((id) => !_byId.containsKey(id));
    _cursor = _history.isEmpty ? -1 : _history.length - 1;
    _aimCamera();
    notifyListeners();
  }

  void _ingest(List<GraphNode> nodes, List<GraphEdge> edges) {
    _nodes = List.unmodifiable(nodes);
    _edges = List.unmodifiable(edges);
    _byId
      ..clear()
      ..addEntries(nodes.map((n) => MapEntry(n.id, n)));
    _layout = layoutGraph(nodes);
    _graphRevision++;
  }

  GraphNode? nodeById(String id) => _byId[id];

  /// Selects [nodeId] and flies the camera to it. Truncates any forward
  /// history, the way a browser does when you navigate after going back.
  void focus(String nodeId) {
    if (!_byId.containsKey(nodeId) || selected == nodeId) {
      return;
    }
    if (_cursor < _history.length - 1) {
      _history.removeRange(_cursor + 1, _history.length);
    }
    _history.add(nodeId);
    _cursor = _history.length - 1;
    _aimCamera();
    notifyListeners();
  }

  void back() {
    if (!canGoBack) return;
    _cursor--;
    _aimCamera();
    notifyListeners();
  }

  void forward() {
    if (!canGoForward) return;
    _cursor++;
    _aimCamera();
    notifyListeners();
  }

  void _aimCamera() {
    final id = selected;
    if (id == null) return;
    final point = _layout[id];
    if (point == null) return;
    camera.focusOn(
      point,
      zoom: _byId[id]!.kind == GraphNodeKind.hub ? 1.0 : 1.5,
    );
  }

  GraphNeighbours neighbours(String nodeId) {
    final incoming = <GraphNode>[];
    final outgoing = <GraphNode>[];
    for (final edge in _edges) {
      if (edge.targetId == nodeId && _byId[edge.sourceId] != null) {
        incoming.add(_byId[edge.sourceId]!);
      } else if (edge.sourceId == nodeId && _byId[edge.targetId] != null) {
        outgoing.add(_byId[edge.targetId]!);
      }
    }
    return (incoming: incoming, outgoing: outgoing);
  }

  /// Shortest path from the hub (or the first node) to the selection.
  List<GraphNode> get breadcrumb {
    final target = selected;
    if (target == null || _nodes.isEmpty) return const [];

    final root = _nodes
        .firstWhere(
          (n) => n.kind == GraphNodeKind.hub,
          orElse: () => _nodes.first,
        )
        .id;
    if (root == target) return [_byId[root]!];

    final cameFrom = <String, String>{};
    final queue = <String>[root];
    final seen = <String>{root};

    while (queue.isNotEmpty) {
      final at = queue.removeAt(0);
      if (at == target) break;
      for (final edge in _edges) {
        final next = edge.sourceId == at
            ? edge.targetId
            : edge.targetId == at
            ? edge.sourceId
            : null;
        if (next == null || !seen.add(next) || !_byId.containsKey(next)) {
          continue;
        }
        cameFrom[next] = at;
        queue.add(next);
      }
    }

    if (!cameFrom.containsKey(target)) return [_byId[target]!];

    final path = <GraphNode>[];
    String? walk = target;
    while (walk != null) {
      path.insert(0, _byId[walk]!);
      walk = cameFrom[walk];
    }
    return path;
  }

  Offset? projectToScreen(String nodeId) => projector?.call(nodeId);
}
