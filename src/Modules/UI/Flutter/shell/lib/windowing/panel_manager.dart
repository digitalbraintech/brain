import 'dart:math' as math;
import 'dart:ui';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart' show Icons;
import 'package:flutter/widgets.dart' show IconData;

enum WindowPanelState { normal, minimized }

/// One free-floating window on the demo canvas. Body is pure Flutter (no RFW / no edge).
final class WindowPanel {
  WindowPanel({
    required this.id,
    required this.title,
    required this.rect,
    required this.z,
    required this.kind,
    this.state = WindowPanelState.normal,
  });

  final String id;
  String title;
  Rect rect;
  int z;
  WindowPanelState state;

  /// Demo content kind rendered by [WindowingScreen].
  final WindowPanelKind kind;
}

enum WindowPanelKind {
  clock,
  metrics,
  notes,
  activity,
  inspector,
  chart,
  timeChart,
}

IconData windowPanelIcon(WindowPanelKind kind) => switch (kind) {
  WindowPanelKind.clock => Icons.schedule_rounded,
  WindowPanelKind.metrics => Icons.speed_rounded,
  WindowPanelKind.notes => Icons.sticky_note_2_outlined,
  WindowPanelKind.activity => Icons.timeline_rounded,
  WindowPanelKind.inspector => Icons.search_rounded,
  WindowPanelKind.chart => Icons.bar_chart_rounded,
  WindowPanelKind.timeChart => Icons.candlestick_chart_rounded,
};

Color windowPanelAccent(WindowPanelKind kind) => switch (kind) {
  WindowPanelKind.clock => const Color(0xFFE09261),
  WindowPanelKind.metrics => const Color(0xFF7B9BE3),
  WindowPanelKind.notes => const Color(0xFF65C5A0),
  WindowPanelKind.activity => const Color(0xFFC49BFF),
  WindowPanelKind.inspector => const Color(0xFFE8C26A),
  WindowPanelKind.chart => const Color(0xFF5EC8E8),
  WindowPanelKind.timeChart => const Color(0xFF0ECB81),
};

/// In-canvas window manager for the Windowing demo tab.
final class PanelManager extends ChangeNotifier {
  final List<WindowPanel> _panels = <WindowPanel>[];
  int _topZ = 0;
  Size _canvasSize = const Size(1280, 800);

  List<WindowPanel> get panels {
    final ordered = List<WindowPanel>.of(_panels)
      ..sort((a, b) => a.z.compareTo(b.z));
    return List.unmodifiable(ordered);
  }

  List<WindowPanel> get minimized => List.unmodifiable(
    _panels.where((p) => p.state == WindowPanelState.minimized),
  );

  void setCanvasSize(Size size) {
    if (size.width <= 0 || size.height <= 0) return;
    _canvasSize = size;
  }

  void seedDemoPanels() {
    if (_panels.isNotEmpty) return;
    const kinds = <(String, WindowPanelKind)>[
      ('Analog clock', WindowPanelKind.clock),
      ('Live metrics', WindowPanelKind.metrics),
      ('Synapses chart', WindowPanelKind.chart),
      ('BTC / USD', WindowPanelKind.timeChart),
      ('Scratch notes', WindowPanelKind.notes),
      ('Activity strip', WindowPanelKind.activity),
      ('Inspector', WindowPanelKind.inspector),
    ];
    for (var i = 0; i < kinds.length; i++) {
      final (title, kind) = kinds[i];
      _panels.add(
        WindowPanel(
          id: 'demo-$i',
          title: title,
          rect: _cascadeSlot(i),
          z: ++_topZ,
          kind: kind,
        ),
      );
    }
    notifyListeners();
  }

  void addPanel(WindowPanelKind kind) {
    final title = switch (kind) {
      WindowPanelKind.clock => 'Analog clock',
      WindowPanelKind.metrics => 'Live metrics',
      WindowPanelKind.notes => 'Scratch notes',
      WindowPanelKind.activity => 'Activity strip',
      WindowPanelKind.inspector => 'Inspector',
      WindowPanelKind.chart => 'Synapses chart',
      WindowPanelKind.timeChart => 'BTC / USD',
    };
    final id = 'demo-${DateTime.now().microsecondsSinceEpoch}';
    _panels.add(
      WindowPanel(
        id: id,
        title: title,
        rect: _cascadeSlot(_panels.length),
        z: ++_topZ,
        kind: kind,
      ),
    );
    notifyListeners();
  }

  WindowPanel? _byId(String id) {
    for (final p in _panels) {
      if (p.id == id) return p;
    }
    return null;
  }

  Rect _cascadeSlot(int index) {
    const w = 320.0;
    const h = 280.0;
    const gap = 20.0;
    const topMargin = 72.0;
    final columns = math.max(
      1,
      ((_canvasSize.width - gap) / (w + gap)).floor(),
    );
    final col = index % columns;
    final row = index ~/ columns;
    final x = (gap + col * (w + gap)).clamp(
      0.0,
      math.max(0.0, _canvasSize.width - w),
    );
    final y = (topMargin + row * (h + gap)).clamp(
      0.0,
      math.max(0.0, _canvasSize.height - h),
    );
    return Rect.fromLTWH(x.toDouble(), y.toDouble(), w, h);
  }

  void raise(String id) {
    final p = _byId(id);
    if (p == null) return;
    if (p.z != _topZ) {
      p.z = ++_topZ;
      notifyListeners();
    }
  }

  void move(String id, Offset delta) {
    final p = _byId(id);
    if (p == null) return;
    p.rect = _clampToCanvas(p.rect.shift(delta));
    notifyListeners();
  }

  void resize(String id, Size delta) {
    final p = _byId(id);
    if (p == null) return;
    final w = (p.rect.width + delta.width).clamp(220.0, _canvasSize.width);
    final h = (p.rect.height + delta.height).clamp(160.0, _canvasSize.height);
    p.rect = Rect.fromLTWH(p.rect.left, p.rect.top, w, h);
    notifyListeners();
  }

  Rect _clampToCanvas(Rect r) {
    final maxLeft = math.max(0.0, _canvasSize.width - r.width);
    final maxTop = math.max(0.0, _canvasSize.height - r.height);
    return Rect.fromLTWH(
      r.left.clamp(0.0, maxLeft),
      r.top.clamp(0.0, maxTop),
      r.width,
      r.height,
    );
  }

  void toggleMinimize(String id) {
    final p = _byId(id);
    if (p == null) return;
    p.state = p.state == WindowPanelState.minimized
        ? WindowPanelState.normal
        : WindowPanelState.minimized;
    if (p.state == WindowPanelState.normal) {
      p.z = ++_topZ;
    }
    notifyListeners();
  }

  void close(String id) {
    _panels.removeWhere((p) => p.id == id);
    notifyListeners();
  }

  void autoLayout() {
    final visible =
        _panels.where((p) => p.state == WindowPanelState.normal).toList()
          ..sort((a, b) => a.z.compareTo(b.z));
    if (visible.isEmpty) return;

    const margin = 28.0;
    const gap = 16.0;
    final cols = math.max(1, math.sqrt(visible.length).ceil());
    final rows = (visible.length / cols).ceil();
    final cellW = (_canvasSize.width - margin * 2 - gap * (cols - 1)) / cols;
    final cellH = (_canvasSize.height - margin * 2 - gap * (rows - 1)) / rows;

    for (var i = 0; i < visible.length; i++) {
      final r = i ~/ cols;
      final c = i % cols;
      visible[i].rect = Rect.fromLTWH(
        margin + c * (cellW + gap),
        margin + r * (cellH + gap),
        math.max(220.0, cellW),
        math.max(160.0, cellH),
      );
    }
    notifyListeners();
  }

  void resetDemo() {
    _panels.clear();
    _topZ = 0;
    seedDemoPanels();
  }

  void loadScenes(
    List<(String id, String title, WindowPanelKind kind)> scenes,
  ) {
    _panels.clear();
    _topZ = 0;
    for (var i = 0; i < scenes.length; i++) {
      final (id, title, kind) = scenes[i];
      _panels.add(
        WindowPanel(
          id: id,
          title: title,
          rect: _cascadeSlot(i),
          z: ++_topZ,
          kind: kind,
        ),
      );
    }
    notifyListeners();
  }
}
