import 'package:flutter/material.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';
import '../sheet/kit_sheet.dart';

/// Renders a kind-bound interactive surface. Calculator is the built-in kind.
final class KitView extends StatelessWidget {
  const KitView({
    super.key,
    required this.kind,
    required this.display,
    this.phase = 'idle',
    this.onKey,
  });

  final String kind;
  final String display;
  final String phase;
  final ValueChanged<String>? onKey;

  @override
  Widget build(BuildContext context) {
    if (kind.toLowerCase() == 'calculator') {
      return _CalculatorPad(display: display, phase: phase, onKey: onKey);
    }

    if (kind.toLowerCase() == 'spreadsheet') {
      return const KitSheet(
        part: KitSheetPart(
          title: 'Sheet',
          columns: ['A', 'B'],
          rows: [
            ['', ''],
          ],
        ),
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Text('No KitView for kind "$kind"', style: KitType.body),
      ),
    );
  }
}

final class _CalculatorPad extends StatelessWidget {
  const _CalculatorPad({
    required this.display,
    required this.phase,
    this.onKey,
  });

  final String display;
  final String phase;
  final ValueChanged<String>? onKey;

  static const _keys = <List<String>>[
    ['C', 'CE', 'BS', '÷'],
    ['7', '8', '9', '×'],
    ['4', '5', '6', '-'],
    ['1', '2', '3', '+'],
    ['0', '.', '='],
  ];

  @override
  Widget build(BuildContext context) {
    return Material(
      color: KitPalette.surface,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('calculator · $phase', style: KitType.meta),
            const SizedBox(height: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
              decoration: BoxDecoration(
                color: KitPalette.surfaceRaised,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: KitPalette.line),
              ),
              alignment: Alignment.centerRight,
              child: Text(
                display,
                key: const Key('kit_view_display'),
                style: KitType.title.copyWith(fontSize: 28),
              ),
            ),
            const SizedBox(height: 12),
            for (final row in _keys) ...[
              Row(
                children: [
                  for (final key in row)
                    Expanded(
                      flex: key == '0' ? 2 : 1,
                      child: Padding(
                        padding: const EdgeInsets.all(4),
                        child: FilledButton(
                          key: Key('kit_view_key_$key'),
                          onPressed: onKey == null ? null : () => onKey!(key),
                          style: FilledButton.styleFrom(
                            backgroundColor: _isOp(key)
                                ? KitPalette.signal
                                : KitPalette.surfaceRaised,
                            foregroundColor: KitPalette.textPrimary,
                            padding: const EdgeInsets.symmetric(vertical: 16),
                          ),
                          child: Text(key, style: KitType.metaStrong),
                        ),
                      ),
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }

  static bool _isOp(String key) =>
      key == '+' ||
      key == '-' ||
      key == '×' ||
      key == '÷' ||
      key == '=' ||
      key == 'C' ||
      key == 'CE' ||
      key == 'BS';
}
