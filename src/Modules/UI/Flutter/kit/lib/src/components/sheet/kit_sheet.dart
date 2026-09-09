import 'package:flutter/material.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';

/// Product spreadsheet control. Same widget for surfaces and chat bubbles.
final class KitSheet extends StatelessWidget {
  const KitSheet({super.key, required this.part, this.maxHeight = 240});

  final KitSheetPart part;
  final double maxHeight;

  @override
  Widget build(BuildContext context) {
    final columns = part.columns;
    final columnCount = columns.isEmpty ? 1 : columns.length;

    return DecoratedBox(
      key: Key('kit_sheet_${part.title}'),
      decoration: BoxDecoration(
        color: KitPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: KitPalette.line),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(part.title, style: KitType.title),
            const SizedBox(height: 2),
            Text(part.sheetName, style: KitType.meta),
            const SizedBox(height: 10),
            ConstrainedBox(
              constraints: BoxConstraints(maxHeight: maxHeight),
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: SingleChildScrollView(
                  child: Table(
                    defaultColumnWidth: const IntrinsicColumnWidth(),
                    border: TableBorder.all(color: KitPalette.line, width: 1),
                    children: [
                      TableRow(
                        decoration: const BoxDecoration(
                          color: KitPalette.surfaceSunken,
                        ),
                        children: [
                          for (var i = 0; i < columnCount; i++)
                            _SheetCell(
                              text: i < columns.length ? columns[i] : '',
                              header: true,
                            ),
                        ],
                      ),
                      for (final row in part.rows)
                        TableRow(
                          children: [
                            for (var i = 0; i < columnCount; i++)
                              _SheetCell(text: i < row.length ? row[i] : ''),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

final class _SheetCell extends StatelessWidget {
  const _SheetCell({required this.text, this.header = false});

  final String text;
  final bool header;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      child: Text(text, style: header ? KitType.metaStrong : KitType.meta),
    );
  }
}
