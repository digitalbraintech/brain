import 'dart:async';

import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import '../chat/chat_contracts.dart';
import '../demos/shell_chart_demo.dart';
import 'panel_manager.dart';

/// Desk windows from ISurface scenes (`GET /kit/surfaces/desk`). Demo panels if empty.
final class WindowingScreen extends StatefulWidget {
  const WindowingScreen({super.key, this.onReadSurface});

  final ReadSurface? onReadSurface;

  @override
  State<WindowingScreen> createState() => _WindowingScreenState();
}

final class _WindowingScreenState extends State<WindowingScreen> {
  late final PanelManager _manager = PanelManager();

  @override
  void initState() {
    super.initState();
    _manager.seedDemoPanels();
    unawaited(_loadDesk());
  }

  Future<void> _loadDesk() async {
    final read = widget.onReadSurface;
    if (read == null) {
      return;
    }
    try {
      final surface = await read('desk');
      if (!mounted || surface == null || surface.scenes.isEmpty) {
        return;
      }
      _manager.loadScenes([
        for (final scene in surface.scenes)
          (
            scene.surfaceKey,
            scene.title,
            scene.surfaceKey.startsWith('chart:')
                ? WindowPanelKind.chart
                : WindowPanelKind.notes,
          ),
      ]);
    } catch (_) {}
  }

  @override
  void dispose() {
    _manager.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ColoredBox(
      key: const Key('windowing_screen'),
      color: BrainPalette.surfaceSunken,
      child: Stack(
        children: [
          const Positioned.fill(child: _CanvasBackdrop()),
          Positioned.fill(
            child: LayoutBuilder(
              builder: (context, constraints) {
                _manager.setCanvasSize(constraints.biggest);
                return AnimatedBuilder(
                  animation: _manager,
                  builder: (context, _) {
                    final visible = _manager.panels
                        .where((p) => p.state == WindowPanelState.normal)
                        .toList();
                    return Stack(
                      children: [
                        for (final panel in visible)
                          Positioned(
                            key: ValueKey('panel-${panel.id}'),
                            left: panel.rect.left,
                            top: panel.rect.top,
                            width: panel.rect.width,
                            height: panel.rect.height,
                            child: _PanelFrame(
                              panel: panel,
                              onRaise: () => _manager.raise(panel.id),
                              onMove: (d) => _manager.move(panel.id, d),
                              onResize: (d) => _manager.resize(panel.id, d),
                              onMinimize: () =>
                                  _manager.toggleMinimize(panel.id),
                              onClose: () => _manager.close(panel.id),
                            ),
                          ),
                      ],
                    );
                  },
                );
              },
            ),
          ),
          Positioned(
            left: 20,
            top: 16,
            right: 20,
            child: _WindowingToolbar(
              onTidy: _manager.autoLayout,
              onReset: _manager.resetDemo,
              onSpawn: _manager.addPanel,
            ),
          ),
          // Dock sits above panels/toolbar so minimized icons are always visible.
          Positioned(
            left: 0,
            right: 0,
            bottom: 18,
            child: AnimatedBuilder(
              animation: _manager,
              builder: (context, _) => _MacDock(manager: _manager),
            ),
          ),
        ],
      ),
    );
  }
}

final class _CanvasBackdrop extends StatelessWidget {
  const _CanvasBackdrop();

  @override
  Widget build(BuildContext context) {
    return CustomPaint(painter: _GridPainter());
  }
}

final class _GridPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = BrainPalette.line.withValues(alpha: 0.45)
      ..strokeWidth = 1;
    const step = 32.0;
    for (var x = 0.0; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), paint);
    }
    for (var y = 0.0; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), paint);
    }
    final wash = Paint()
      ..shader =
          RadialGradient(
            colors: [
              BrainPalette.owner.withValues(alpha: 0.08),
              Colors.transparent,
            ],
          ).createShader(
            Rect.fromCircle(
              center: Offset(size.width * 0.55, size.height * 0.4),
              radius: size.shortestSide * 0.55,
            ),
          );
    canvas.drawRect(Offset.zero & size, wash);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

final class _WindowingToolbar extends StatelessWidget {
  const _WindowingToolbar({
    required this.onTidy,
    required this.onReset,
    required this.onSpawn,
  });

  final VoidCallback onTidy;
  final VoidCallback onReset;
  final ValueChanged<WindowPanelKind> onSpawn;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        decoration: BoxDecoration(
          color: BrainPalette.surfaceRaised.withValues(alpha: 0.92),
          borderRadius: BorderRadius.circular(14),
          border: Border.all(color: BrainPalette.line),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.3),
              blurRadius: 16,
              offset: const Offset(0, 8),
            ),
          ],
        ),
        child: Row(
          children: [
            const Icon(
              Icons.desktop_windows_outlined,
              size: 18,
              color: BrainPalette.signal,
            ),
            const SizedBox(width: 10),
            const Expanded(
              child: Text(
                'Windowing demo — drag title bars, resize corners, minimize to dock',
                style: BrainType.bodyMuted,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 8),
            PopupMenuButton<WindowPanelKind>(
              tooltip: 'Spawn window',
              onSelected: onSpawn,
              itemBuilder: (context) => [
                for (final kind in WindowPanelKind.values)
                  PopupMenuItem(value: kind, child: Text(kind.name)),
              ],
              child: const Padding(
                padding: EdgeInsets.symmetric(horizontal: 8, vertical: 6),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(Icons.add, size: 16, color: BrainPalette.textPrimary),
                    SizedBox(width: 4),
                    Text('Spawn', style: BrainType.metaStrong),
                  ],
                ),
              ),
            ),
            TextButton.icon(
              onPressed: onTidy,
              icon: const Icon(Icons.grid_view_rounded, size: 16),
              label: const Text('Tidy'),
            ),
            TextButton.icon(
              onPressed: onReset,
              icon: const Icon(Icons.refresh, size: 16),
              label: const Text('Reset'),
            ),
          ],
        ),
      ),
    );
  }
}

final class _PanelFrame extends StatelessWidget {
  const _PanelFrame({
    required this.panel,
    required this.onRaise,
    required this.onMove,
    required this.onResize,
    required this.onMinimize,
    required this.onClose,
  });

  final WindowPanel panel;
  final VoidCallback onRaise;
  final void Function(Offset delta) onMove;
  final void Function(Size delta) onResize;
  final VoidCallback onMinimize;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    return Listener(
      onPointerDown: (_) => onRaise(),
      child: Material(
        color: Colors.transparent,
        child: Container(
          decoration: BoxDecoration(
            color: BrainPalette.surfaceRaised.withValues(alpha: 0.96),
            borderRadius: BorderRadius.circular(16),
            border: Border.all(
              color: BrainPalette.signal.withValues(alpha: 0.28),
            ),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.42),
                blurRadius: 24,
                offset: const Offset(0, 12),
              ),
            ],
          ),
          clipBehavior: Clip.antiAlias,
          child: Stack(
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _titleBar(),
                  Expanded(
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(14, 12, 14, 18),
                      child: _PanelBody(kind: panel.kind),
                    ),
                  ),
                ],
              ),
              Positioned(
                right: 0,
                bottom: 0,
                child: GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onPanUpdate: (d) => onResize(Size(d.delta.dx, d.delta.dy)),
                  child: MouseRegion(
                    cursor: SystemMouseCursors.resizeDownRight,
                    child: Padding(
                      padding: const EdgeInsets.all(6),
                      child: Icon(
                        Icons.south_east,
                        size: 14,
                        color: BrainPalette.textFaint.withValues(alpha: 0.7),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _titleBar() {
    return GestureDetector(
      behavior: HitTestBehavior.opaque,
      onPanStart: (_) => onRaise(),
      onPanUpdate: (d) => onMove(d.delta),
      child: Container(
        height: 36,
        padding: const EdgeInsets.symmetric(horizontal: 12),
        decoration: BoxDecoration(
          color: Colors.white.withValues(alpha: 0.04),
          border: Border(
            bottom: BorderSide(color: Colors.white.withValues(alpha: 0.06)),
          ),
        ),
        child: Row(
          children: [
            Container(
              width: 8,
              height: 8,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: BrainPalette.signal.withValues(alpha: 0.85),
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                panel.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: BrainType.cardTitle.copyWith(fontSize: 13),
              ),
            ),
            _chromeButton(Icons.remove, onMinimize, 'Minimize'),
            _chromeButton(Icons.close, onClose, 'Close'),
          ],
        ),
      ),
    );
  }

  Widget _chromeButton(IconData icon, VoidCallback onTap, String tip) {
    return Tooltip(
      message: tip,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(6),
        child: Padding(
          padding: const EdgeInsets.all(4),
          child: Icon(icon, size: 16, color: BrainPalette.textMuted),
        ),
      ),
    );
  }
}

final class _PanelBody extends StatelessWidget {
  const _PanelBody({required this.kind});

  final WindowPanelKind kind;

  @override
  Widget build(BuildContext context) {
    return switch (kind) {
      WindowPanelKind.clock => const Center(
        child: SizedBox(width: 150, height: 150, child: KitClock()),
      ),
      WindowPanelKind.metrics => const _MetricsBody(),
      WindowPanelKind.notes => const _NotesBody(),
      WindowPanelKind.activity => const _ActivityBody(),
      WindowPanelKind.inspector => const _InspectorBody(),
      WindowPanelKind.chart => const ShellBarChartDemo(height: 200),
      WindowPanelKind.timeChart => const Padding(
        padding: EdgeInsets.symmetric(horizontal: 4),
        child: KitTimeChart(),
      ),
    };
  }
}

final class _MetricsBody extends StatelessWidget {
  const _MetricsBody();

  @override
  Widget build(BuildContext context) {
    const rows = <(String, String)>[
      ('Neurons', '24'),
      ('Synapses/min', '186'),
      ('p50 latency', '12ms'),
      ('Open windows', '5'),
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final row in rows)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Row(
              children: [
                Expanded(child: Text(row.$1, style: BrainType.bodyMuted)),
                Text(row.$2, style: BrainType.metric.copyWith(fontSize: 15)),
              ],
            ),
          ),
      ],
    );
  }
}

final class _NotesBody extends StatelessWidget {
  const _NotesBody();

  @override
  Widget build(BuildContext context) {
    return Text(
      'Demo notes panel.\n\n'
      'Drag the title bar to move.\n'
      'Use the corner grip to resize.\n'
      'Minimize lands in the dock strip.',
      style: BrainType.bodyMuted,
    );
  }
}

final class _ActivityBody extends StatelessWidget {
  const _ActivityBody();

  @override
  Widget build(BuildContext context) {
    const events = [
      'ChatTurnCommitted · seq 42',
      'AuthorizationRequired · google',
      'TopologyPulse · 3 grains',
      'TimerScheduled · countdown',
    ];
    return ListView(
      children: [
        for (final e in events)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
              decoration: BoxDecoration(
                color: BrainPalette.surfaceSunken,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: BrainPalette.line),
              ),
              child: Text(e, style: BrainType.meta),
            ),
          ),
      ],
    );
  }
}

final class _InspectorBody extends StatelessWidget {
  const _InspectorBody();

  @override
  Widget build(BuildContext context) {
    const fields = <(String, String)>[
      ('id', 'owner/main'),
      ('kind', 'IChat'),
      ('state', 'active'),
      ('depth', '2'),
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Selection', style: BrainType.cardTitle),
        const SizedBox(height: 12),
        for (final f in fields)
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: Row(
              children: [
                SizedBox(width: 72, child: Text(f.$1, style: BrainType.meta)),
                Expanded(child: Text(f.$2, style: BrainType.body)),
              ],
            ),
          ),
      ],
    );
  }
}

/// macOS-style bottom dock: large app icons for minimized windows.
final class _MacDock extends StatelessWidget {
  const _MacDock({required this.manager});

  final PanelManager manager;

  @override
  Widget build(BuildContext context) {
    final docked = manager.minimized;
    if (docked.isEmpty) return const SizedBox.shrink();

    return Center(
      child: Material(
        color: Colors.transparent,
        child: Container(
          key: const Key('windowing_dock'),
          padding: const EdgeInsets.fromLTRB(14, 10, 14, 10),
          decoration: BoxDecoration(
            color: const Color(0xCC1B1F2A),
            borderRadius: BorderRadius.circular(22),
            border: Border.all(color: Colors.white.withValues(alpha: 0.12)),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.45),
                blurRadius: 28,
                offset: const Offset(0, 12),
              ),
            ],
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (var i = 0; i < docked.length; i++) ...[
                if (i > 0) const SizedBox(width: 10),
                _DockIcon(
                  panel: docked[i],
                  onTap: () => manager.toggleMinimize(docked[i].id),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

final class _DockIcon extends StatefulWidget {
  const _DockIcon({required this.panel, required this.onTap});

  final WindowPanel panel;
  final VoidCallback onTap;

  @override
  State<_DockIcon> createState() => _DockIconState();
}

final class _DockIconState extends State<_DockIcon> {
  bool _hover = false;

  @override
  Widget build(BuildContext context) {
    final accent = windowPanelAccent(widget.panel.kind);
    final icon = windowPanelIcon(widget.panel.kind);
    final scale = _hover ? 1.14 : 1.0;

    return Tooltip(
      message: widget.panel.title,
      child: MouseRegion(
        onEnter: (_) => setState(() => _hover = true),
        onExit: (_) => setState(() => _hover = false),
        child: GestureDetector(
          onTap: widget.onTap,
          child: AnimatedScale(
            scale: scale,
            duration: const Duration(milliseconds: 140),
            curve: Curves.easeOutCubic,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 52,
                  height: 52,
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(14),
                    gradient: LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [
                        accent.withValues(alpha: 0.95),
                        accent.withValues(alpha: 0.55),
                      ],
                    ),
                    boxShadow: [
                      BoxShadow(
                        color: accent.withValues(alpha: 0.35),
                        blurRadius: 12,
                        offset: const Offset(0, 4),
                      ),
                    ],
                  ),
                  child: Icon(icon, color: Colors.white, size: 26),
                ),
                const SizedBox(height: 4),
                // Reflection/dot like macOS running indicator.
                Container(
                  width: 4,
                  height: 4,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: Colors.white.withValues(alpha: 0.75),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
