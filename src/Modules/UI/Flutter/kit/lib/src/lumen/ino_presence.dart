import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'lumen_palette.dart';

/// Presentation states driven by observed activity, never a source of work.
enum InoPresenceState {
  idle,
  listening,
  accepted,
  working,
  reading,
  searching,
  waiting,
  completed,
  attention,
  disconnected,
}

/// The approved green Ino face. This programmatic artwork is a small stand-in
/// until an authored Rive asset is audited; it has no transport or domain state.
final class InoPresence extends StatefulWidget {
  const InoPresence({
    super.key,
    this.state = InoPresenceState.idle,
    this.size = 92,
  });

  final InoPresenceState state;
  final double size;

  @override
  State<InoPresence> createState() => _InoPresenceState();
}

final class _InoPresenceState extends State<InoPresence>
    with SingleTickerProviderStateMixin {
  late final AnimationController _motion = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 2200),
  );

  bool get _active => switch (widget.state) {
    InoPresenceState.listening ||
    InoPresenceState.accepted ||
    InoPresenceState.working ||
    InoPresenceState.reading ||
    InoPresenceState.searching => true,
    _ => false,
  };

  void _syncMotion() {
    if (_active && !MediaQuery.disableAnimationsOf(context)) {
      if (!_motion.isAnimating) _motion.repeat(reverse: true);
    } else {
      _motion.stop();
      _motion.value = 0;
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncMotion();
  }

  @override
  void didUpdateWidget(InoPresence oldWidget) {
    super.didUpdateWidget(oldWidget);
    _syncMotion();
  }

  @override
  void dispose() {
    _motion.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Semantics(
    image: true,
    label:
        'Ino, ${switch (widget.state) {
          InoPresenceState.idle => 'ready when you are',
          InoPresenceState.listening => 'listening',
          InoPresenceState.accepted => 'request accepted',
          InoPresenceState.working => 'working',
          InoPresenceState.reading => 'reading',
          InoPresenceState.searching => 'searching',
          InoPresenceState.waiting => 'waiting for you',
          InoPresenceState.completed => 'completed',
          InoPresenceState.attention => 'needs attention',
          InoPresenceState.disconnected => 'disconnected',
        }}',
    child: RepaintBoundary(
      child: SizedBox.square(
        dimension: widget.size,
        child: AnimatedBuilder(
          animation: _motion,
          builder: (context, _) => CustomPaint(
            painter: _InoPainter(state: widget.state, phase: _motion.value),
          ),
        ),
      ),
    ),
  );
}

final class _InoPainter extends CustomPainter {
  const _InoPainter({required this.state, required this.phase});

  final InoPresenceState state;
  final double phase;

  @override
  void paint(Canvas canvas, Size size) {
    final edge = math.min(size.width, size.height);
    canvas.save();
    canvas.scale(edge / 100);
    final rect = Rect.fromLTWH(7, 7 + phase * 1.4, 86, 86);
    final shape = RRect.fromRectAndRadius(rect, const Radius.circular(29));
    final path = Path()..addRRect(shape);
    canvas.drawShadow(path, const Color(0x243C6D50), 9, false);
    canvas.drawRRect(
      shape,
      Paint()
        ..shader = LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: state == InoPresenceState.disconnected
              ? const [Color(0xFFE2E8E1), Color(0xFFC5CEC4)]
              : const [Color(0xFFE9F4DF), Color(0xFFC2D9B7)],
        ).createShader(rect),
    );
    canvas.drawRRect(
      shape,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.1
        ..color = const Color(0xFFD0E0C7),
    );
    final face = Paint()
      ..color = state == InoPresenceState.attention
          ? LumenPalette.warning
          : const Color(0xFF405E48)
      ..strokeWidth = 3.8
      ..strokeCap = StrokeCap.round
      ..style = PaintingStyle.stroke;
    final looking = state == InoPresenceState.searching ? phase * 3 - 1.5 : 0.0;
    final eyeY = 43 + phase;
    for (final x in [38.0, 62.0]) {
      if (state == InoPresenceState.completed) {
        canvas.drawArc(
          Rect.fromCenter(center: Offset(x, eyeY), width: 9, height: 7),
          math.pi,
          math.pi,
          false,
          face,
        );
      } else {
        canvas.drawLine(
          Offset(x + looking, eyeY - 2),
          Offset(x + looking, eyeY + 3),
          face,
        );
      }
    }
    final mouth = Path()..moveTo(43, 61 + phase);
    if (state == InoPresenceState.attention ||
        state == InoPresenceState.disconnected) {
      mouth.lineTo(57, 61 + phase);
    } else {
      mouth.quadraticBezierTo(50, 67 + phase, 57, 61 + phase);
    }
    canvas.drawPath(mouth, face..strokeWidth = 2.6);
    if (state == InoPresenceState.waiting) {
      canvas.drawCircle(
        const Offset(86, 16),
        9,
        Paint()..color = LumenPalette.warning,
      );
      canvas.drawLine(
        const Offset(86, 11),
        const Offset(86, 17),
        Paint()
          ..color = Colors.white
          ..strokeWidth = 2.2
          ..strokeCap = StrokeCap.round,
      );
      canvas.drawCircle(
        const Offset(86, 21),
        1.2,
        Paint()..color = Colors.white,
      );
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(_InoPainter oldDelegate) =>
      state != oldDelegate.state || phase != oldDelegate.phase;
}
