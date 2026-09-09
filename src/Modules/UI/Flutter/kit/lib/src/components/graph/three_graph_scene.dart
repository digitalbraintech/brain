import 'dart:math' as math;

import 'package:flutter/widgets.dart';
import 'package:three_js/three_js.dart' as three;

import '../../theme/kit_theme.dart';
import 'graph_camera.dart';
import 'graph_models.dart';
import 'graph_scene.dart';

/// three_js implementation of [GraphScene].
///
/// Mesh construction follows the shell brain canvas pattern: a node sphere plus
/// an additive halo per node, and a quadratic-bezier line per edge bowing
/// outward past the shell. Node positions come from the caller's layout map
/// rather than a hardcoded topology.
final class ThreeGraphScene implements GraphScene, AnimatedGraphScene {
  ThreeGraphScene() {
    _threeJs = three.ThreeJS(
      onSetupComplete: () {
        if (_disposed) {
          _disposeRenderer();
        } else {
          _initialized.value = true;
        }
      },
      setup: _setup,
      settings: three.Settings(
        antialias: true,
        clearColor: KitPalette.surfaceSunken.toARGB32() & 0xFFFFFF,
        clearAlpha: 1,
      ),
    );
    // ThreeJS.dispose reads these late-final fields even before setup.
    _threeJs.scene = three.Scene();
    _threeJs.camera = three.PerspectiveCamera(45, 1, 0.01, 100);
  }

  static const _shellRadius = 1.55;
  static const _hubSize = 0.13;
  static const _nodeSize = 0.075;
  static const _haloRatio = 3.0;
  static const _cameraDistance = 4.6;

  late final three.ThreeJS _threeJs;
  final three.Raycaster _raycaster = three.Raycaster();

  final List<three.Mesh> _nodeMeshes = [];
  final Map<String, three.Mesh> _meshById = {};
  three.Group? _nodeGroup;
  three.Group? _edgeGroup;
  three.Mesh? _pulseMesh;
  GraphPulse? _pulse;
  double _pulseTime = 0;

  bool _ready = false;
  bool _disposed = false;
  bool _initializationStarted = false;
  bool _rendererDisposed = false;
  Size? _viewSize;
  final _initialized = ValueNotifier(false);

  // Buffered until the renderer finishes setup.
  List<GraphNode> _nodes = const [];
  List<GraphEdge> _edges = const [];
  Map<String, GraphPoint> _layout = const {};
  GraphCameraState _pose = const GraphCameraState(
    yaw: 0.5,
    pitch: -0.18,
    zoom: 1,
  );

  Future<void> _setup() async {
    if (_disposed) return;
    _threeJs.camera.aspect = _threeJs.width / math.max(_threeJs.height, 1);
    _threeJs.camera.updateProjectionMatrix();
    _nodeGroup = three.Group();
    _edgeGroup = three.Group();
    _threeJs.scene.add(_nodeGroup!);
    _threeJs.scene.add(_edgeGroup!);
    _ready = true;
    _rebuild();
    applyCamera(_pose);
  }

  @override
  Future<void> load(
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Map<String, GraphPoint> layout,
  ) async {
    _nodes = nodes;
    _edges = edges;
    _layout = layout;
    if (_ready) _rebuild();
  }

  void _rebuild() {
    if (!_ready || _disposed) return;

    for (final mesh in _nodeMeshes) {
      _nodeGroup!.remove(mesh);
      _disposeObject(mesh);
    }
    _nodeMeshes.clear();
    _meshById.clear();
    for (final edge in List<three.Object3D>.of(_edgeGroup!.children)) {
      _edgeGroup!.remove(edge);
      _disposeObject(edge);
    }

    for (final node in _nodes) {
      final point = _layout[node.id];
      if (point == null) continue;

      final colour = _colourFor(node);
      final hub = node.kind == GraphNodeKind.hub;
      final module = node.kind == GraphNodeKind.module;
      final size = hub
          ? _hubSize
          : module
          ? 0.48
          : _nodeSize;
      final scale = hub ? 0.0 : _shellRadius;

      final mesh = three.Mesh(
        three.SphereGeometry(size, 20, 20),
        three.MeshBasicMaterial.fromMap({
          'color': colour,
          'transparent': true,
          'opacity': module
              ? 0.035
              : node.dimmed
              ? 0.4
              : 0.9,
          'depthWrite': !module,
        }),
      )..position.setValues(point.x * scale, point.y * scale, point.z * scale);
      mesh.userData['nodeId'] = node.id;
      mesh.userData['module'] = module;

      if (!module) {
        final halo = three.Mesh(
          three.SphereGeometry(size * _haloRatio, 18, 18),
          three.MeshBasicMaterial.fromMap({
            'color': colour,
            'transparent': true,
            'opacity': 0.16,
            'blending': three.AdditiveBlending,
            'depthWrite': false,
          }),
        );
        mesh.add(halo);
      }

      _nodeGroup!.add(mesh);
      _nodeMeshes.add(mesh);
      _meshById[node.id] = mesh;
    }

    for (final edge in _edges) {
      final a = _layout[edge.sourceId];
      final b = _layout[edge.targetId];
      if (a == null || b == null) continue;
      _edgeGroup!.add(_edgeLine(a, b, edge));
    }
    _ensurePulse();
  }

  three.Line _edgeLine(GraphPoint a, GraphPoint b, GraphEdge edge) {
    final from = three.Vector3(
      a.x * _shellRadius,
      a.y * _shellRadius,
      a.z * _shellRadius,
    );
    final to = three.Vector3(
      b.x * _shellRadius,
      b.y * _shellRadius,
      b.z * _shellRadius,
    );

    return three.Line(
      three.BufferGeometry().setFromPoints(_curve(from, to).getPoints(32)),
      three.LineBasicMaterial.fromMap({
        'color': edge.decorated ? 0x65C5A0 : 0x7B9BE3,
        'transparent': true,
        'opacity': edge.dotted
            ? 0.12
            : edge.decorated
            ? 0.65
            : 0.34,
        'blending': three.AdditiveBlending,
      }),
    );
  }

  three.QuadraticBezierCurve3 _curve(three.Vector3 from, three.Vector3 to) {
    // Bow outward consistently for both the synapse and its moving signal.
    var mx = (from.x + to.x) * 0.5;
    var my = (from.y + to.y) * 0.5;
    var mz = (from.z + to.z) * 0.5;
    final length = math.sqrt(mx * mx + my * my + mz * mz);
    if (length > 1e-6) {
      final arc = _shellRadius + 0.3;
      mx = mx / length * arc;
      my = my / length * arc;
      mz = mz / length * arc;
    }

    return three.QuadraticBezierCurve3(from, three.Vector3(mx, my, mz), to);
  }

  @override
  void setPulse(GraphPulse? pulse) {
    if (_pulse?.signature == pulse?.signature) return;
    _pulse = pulse;
    _pulseTime = 0;
    _ensurePulse();
  }

  void _ensurePulse() {
    if (!_ready || _disposed) return;
    _pulseMesh ??= three.Mesh(
      three.SphereGeometry(0.04, 16, 16),
      three.MeshBasicMaterial.fromMap({'color': 0xFFF0BD}),
    );
    if (_pulseMesh!.parent == null) _threeJs.scene.add(_pulseMesh!);
    _pulseMesh!.visible = false;
  }

  @override
  void advance(double seconds) {
    if (!_ready || _disposed || _pulseMesh == null) return;
    final pulse = _pulse;
    final from = _meshById[pulse?.fromId];
    final to = _meshById[pulse?.toId];
    if (pulse == null || from == null || to == null) {
      _pulseMesh!.visible = false;
      return;
    }
    _pulseTime += seconds;
    // A finite pulse: pausing playback leaves a stable graph, not a live signal.
    final progress = (_pulseTime / 1.35).clamp(0.0, 1.0);
    _pulseMesh!.visible = progress < 1;
    final point =
        _curve(from.position, to.position).getPoint(progress) as three.Vector3;
    _pulseMesh!.position.setValues(point.x, point.y, point.z);
  }

  /// Cluster-stable colour. Hubs are always the kit's signal cyan.
  int _colourFor(GraphNode node) {
    if (node.kind == GraphNodeKind.hub) return 0x3DDCFF;
    const palette = [
      0x7B9BE3,
      0x65C5A0,
      0xE09261,
      0xE8C56A,
      0xC49BFF,
      0x5EC8E8,
    ];
    final key = node.cluster ?? node.id;
    var hash = 0x811c9dc5;
    for (final unit in key.codeUnits) {
      hash ^= unit;
      hash = (hash * 0x01000193) & 0xFFFFFFFF;
    }
    return palette[hash % palette.length];
  }

  @override
  void applyCamera(GraphCameraState state) {
    _pose = state;
    if (!_ready || _disposed) return;

    // Inverse of GraphCamera.focusOn: place the eye along the focused node's
    // own direction, so that node ends up facing the viewer.
    final distance = _cameraDistance / state.zoom;
    final cosPitch = math.cos(state.pitch);
    _threeJs.camera.position.setValues(
      -math.sin(state.yaw) * cosPitch * distance,
      -math.sin(state.pitch) * distance,
      math.cos(state.yaw) * cosPitch * distance,
    );
    _threeJs.camera.lookAt(three.Vector3(0, 0, 0));
  }

  @override
  String? pick(Offset local) {
    if (!_ready || _disposed || _nodeMeshes.isEmpty) return null;
    final width = _viewSize?.width ?? _threeJs.width;
    final height = _viewSize?.height ?? _threeJs.height;
    if (width == 0 || height == 0) return null;

    _raycaster.setFromCamera(
      three.Vector2((local.dx / width) * 2 - 1, -((local.dy / height) * 2 - 1)),
      _threeJs.camera,
    );

    final hits = _raycaster.intersectObjects(
      _nodeMeshes.cast<three.Object3D>(),
      false,
    );
    if (hits.isEmpty) return null;
    // An envelope must not intercept the neurons it contains. It remains
    // selectable when the ray does not hit any contained neuron.
    final hit = hits.firstWhere(
      (hit) => hit.object?.userData['module'] != true,
      orElse: () => hits.first,
    );
    return hit.object?.userData['nodeId'] as String?;
  }

  @override
  Offset? project(String nodeId) {
    if (!_ready || _disposed) return null;
    final mesh = _meshById[nodeId];
    if (mesh == null) return null;

    final v = three.Vector3(mesh.position.x, mesh.position.y, mesh.position.z)
      ..project(_threeJs.camera);
    if (v.z > 1) return null;

    return Offset(
      (v.x * 0.5 + 0.5) * (_viewSize?.width ?? _threeJs.width),
      (-v.y * 0.5 + 0.5) * (_viewSize?.height ?? _threeJs.height),
    );
  }

  @override
  Widget build(BuildContext context) => ValueListenableBuilder<bool>(
    valueListenable: _initialized,
    builder: (context, _, _) => LayoutBuilder(
      builder: (context, constraints) {
        final size = constraints.biggest;
        if (_viewSize != size && _ready && !_disposed && size.height > 0) {
          _threeJs.camera.aspect = size.width / size.height;
          _threeJs.camera.updateProjectionMatrix();
        }
        _viewSize = size;
        _initializationStarted = true;
        // The embedded graph uses its pane dimensions, not the whole app window.
        return MediaQuery(
          data: MediaQuery.of(context).copyWith(size: size),
          child: _threeJs.build(),
        );
      },
    ),
  );

  @override
  void dispose() {
    if (_disposed) return;
    _disposed = true;
    _initialized.dispose();
    if (!_ready) {
      // The SDK schedules native initialization without a cancellation hook.
      // Once started, let it finish and dispose from onSetupComplete; disposing
      // sooner clears dimensions still needed by its pending async work.
      if (!_initializationStarted) _disposeRenderer();
      return;
    }
    for (final mesh in _nodeMeshes) {
      _nodeGroup!.remove(mesh);
      _disposeObject(mesh);
    }
    for (final edge in List<three.Object3D>.of(_edgeGroup!.children)) {
      _edgeGroup!.remove(edge);
      _disposeObject(edge);
    }
    if (_pulseMesh case final pulse?) {
      _threeJs.scene.remove(pulse);
      _disposeObject(pulse);
    }
    _nodeMeshes.clear();
    _meshById.clear();
    _disposeRenderer();
  }

  void _disposeRenderer() {
    if (_rendererDisposed) return;
    _rendererDisposed = true;
    _threeJs.dispose();
  }

  void _disposeObject(three.Object3D object) {
    for (final child in List<three.Object3D>.of(object.children)) {
      object.remove(child);
      _disposeObject(child);
    }
    if (object is three.Mesh) {
      object.geometry?.dispose();
      object.material?.dispose();
    } else if (object is three.Line) {
      object.geometry?.dispose();
      object.material?.dispose();
    }
  }
}
