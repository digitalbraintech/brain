import 'dart:math' as math;

import 'graph_models.dart';

/// Immutable camera pose.
final class GraphCameraState {
  const GraphCameraState({
    required this.yaw,
    required this.pitch,
    required this.zoom,
  });

  final double yaw;
  final double pitch;
  final double zoom;

  GraphCameraState copyWith({double? yaw, double? pitch, double? zoom}) =>
      GraphCameraState(
        yaw: yaw ?? this.yaw,
        pitch: pitch ?? this.pitch,
        zoom: zoom ?? this.zoom,
      );
}

/// Orbit camera with an eased target.
///
/// [tick] is driven by the view's frame callback; everything here is pure so it
/// can be exercised without a GL context.
final class GraphCamera {
  GraphCamera({GraphCameraState? initial})
    : _current = initial ?? _defaultPose,
      _target = initial ?? _defaultPose;

  static const _defaultPose = GraphCameraState(yaw: 0.5, pitch: -0.18, zoom: 1);
  static const _minPitch = -1.2;
  static const _maxPitch = 1.2;
  static const _minZoom = 0.5;
  static const _maxZoom = 3.0;
  static const _settleEpsilon = 1e-3;

  GraphCameraState _current;
  GraphCameraState _target;

  GraphCameraState get current => _current;
  GraphCameraState get target => _target;

  bool get settled =>
      (_target.yaw - _current.yaw).abs() < _settleEpsilon &&
      (_target.pitch - _current.pitch).abs() < _settleEpsilon &&
      (_target.zoom - _current.zoom).abs() < _settleEpsilon;

  /// Drag deltas in logical pixels.
  void orbitBy(double dx, double dy) {
    _target = _target.copyWith(
      yaw: _target.yaw + dx * 0.008,
      pitch: (_target.pitch + dy * 0.008).clamp(_minPitch, _maxPitch),
    );
  }

  void zoomBy(double factor) {
    _target = _target.copyWith(
      zoom: (_target.zoom * factor).clamp(_minZoom, _maxZoom),
    );
  }

  /// Turns [point] to face the viewer. The origin has no facing direction, so
  /// focusing it only changes zoom.
  void focusOn(GraphPoint point, {double? zoom}) {
    final radius = math.sqrt(
      point.x * point.x + point.y * point.y + point.z * point.z,
    );
    if (radius < 1e-6) {
      if (zoom != null) {
        _target = _target.copyWith(zoom: zoom.clamp(_minZoom, _maxZoom));
      }
      return;
    }

    final planar = math.sqrt(point.x * point.x + point.z * point.z);
    final rawYaw = -math.atan2(point.x, point.z);
    final pitch = (-math.atan2(point.y, planar)).clamp(_minPitch, _maxPitch);

    // Unwrap onto the turn nearest the current pose so the camera takes the
    // short way round instead of unwinding several full rotations.
    const turn = math.pi * 2;
    final winding = ((_current.yaw - rawYaw) / turn).roundToDouble();

    _target = GraphCameraState(
      yaw: rawYaw + winding * turn,
      pitch: pitch,
      zoom: (zoom ?? _target.zoom).clamp(_minZoom, _maxZoom),
    );
  }

  /// Eases [current] toward [target]. Frame-rate independent.
  void tick(double dt) {
    if (settled) {
      _current = _target;
      return;
    }
    final k = 1 - math.pow(0.0015, dt.clamp(0.0, 0.1)).toDouble();
    _current = GraphCameraState(
      yaw: _current.yaw + (_target.yaw - _current.yaw) * k,
      pitch: _current.pitch + (_target.pitch - _current.pitch) * k,
      zoom: _current.zoom + (_target.zoom - _current.zoom) * k,
    );
  }
}
