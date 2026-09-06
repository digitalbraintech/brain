import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

// 1x1 transparent PNG, the smallest valid PNG payload.
final _tinyPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
);

// Image.memory decodes through the real platform codec, which the fake-time
// test clock never advances — pumpAndSettle would hang forever on the
// in-progress frameBuilder placeholder's spinner, so pump a bounded number
// of frames instead of settling.
Future<void> _pumpImageCard(WidgetTester tester) async {
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 10));
  }
}

void main() {
  testWidgets('a chart-ref card fetches its chart and renders KitChart', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);
    final requestedNames = <String>[];

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onSend: (_) async {},
        onReadChart: (name) async {
          requestedNames.add(name);
          return const ChatChartOffer(
            title: 'Test chart',
            points: [ChatChartPoint(label: 'Mon', value: 3)],
          );
        },
      ),
    );

    turns.add(
      shellTurn(
        1,
        false,
        'here is your chart',
        cards: const [
          KitCardRef(kind: 'chart', name: 'daily-sales', caption: 'Test chart'),
        ],
      ),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();

    expect(requestedNames, isEmpty);
    await tester.tap(find.text('Open attachment'));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('kit_chart_Test chart')), findsOneWidget);
    expect(requestedNames, ['daily-sales']);
    await drainShellTimers(tester);
  });

  testWidgets('a spreadsheet-ref card fetches its sheet and renders KitSheet', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);
    final requestedNames = <String>[];

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onSend: (_) async {},
        onReadSpreadsheet: (name) async {
          requestedNames.add(name);
          return const ChatSpreadsheetOffer(
            title: 'Yesterday',
            columns: ['Item', 'Qty'],
            rows: [
              ['Shoes', '2'],
            ],
          );
        },
      ),
    );
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pump();

    turns.add(
      shellTurn(
        1,
        false,
        'here is your sheet',
        cards: const [
          KitCardRef(
            kind: 'spreadsheet',
            name: 'sheet-abc',
            caption: 'Yesterday',
          ),
        ],
      ),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('kit_sheet_Yesterday')), findsOneWidget);
    expect(requestedNames, ['sheet-abc']);
    await drainShellTimers(tester);
  });

  testWidgets('an image-ref card fetches its bytes and renders KitImage', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);
    final requestedNames = <String>[];

    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onSend: (_) async {},
        onReadImageBytes: (name) async {
          requestedNames.add(name);
          return _tinyPng;
        },
      ),
    );
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pump();

    turns.add(
      shellTurn(
        2,
        false,
        'here is your image',
        cards: const [
          KitCardRef(kind: 'image', name: 'sunset', caption: 'Sunset over bay'),
        ],
      ),
    );
    await _pumpImageCard(tester);

    expect(find.byKey(const Key('kit_image_Sunset over bay')), findsOneWidget);
    expect(requestedNames, ['sunset']);
    await drainShellTimers(tester);
  });

  testWidgets(
    'a disconnected shell renders chart and image captions without crashing',
    (tester) async {
      await prepareShellSurface(tester);
      final turns = StreamController<ChatTurnEvent>();
      addTearDown(turns.close);

      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          turns: turns.stream,
          onSend: (_) async {},
        ),
      );
      await enterOldUi(tester);
      await tester.tap(find.byKey(const Key('destination_chat')));
      await tester.pump();

      turns.add(
        shellTurn(
          3,
          false,
          'offline cards',
          cards: const [
            KitCardRef(
              kind: 'chart',
              name: 'daily-sales',
              caption: 'Test chart',
            ),
            KitCardRef(kind: 'image', name: 'sunset', caption: 'Sunset shot'),
          ],
        ),
      );
      await enterOldUi(tester);
      await tester.pumpAndSettle();

      expect(
        find.byKey(const Key('kit_chart_ref_offline_daily-sales')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('kit_image_ref_offline_sunset')),
        findsOneWidget,
      );
      expect(find.text('Test chart'), findsOneWidget);
      expect(find.text('Sunset shot'), findsOneWidget);
      await drainShellTimers(tester);
    },
  );
}
