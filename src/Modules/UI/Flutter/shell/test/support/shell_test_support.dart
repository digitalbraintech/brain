import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

// Shared fixtures for shell widget tests.

ChatTurnEvent shellTurn(
  int sequence,
  bool fromUser,
  String text, {
  String? signal,
  String? commandId,
  String? status,
  List<KitCardRef> cards = const [],
}) => ChatTurnEvent(
  sequence: sequence,
  fromUser: fromUser,
  text: text,
  commandId: commandId ?? 'c$sequence',
  signal: signal ?? (fromUser ? 'UserMessaged' : 'Responded'),
  neuronId: 'chat:owner/main',
  caller: 'chat:owner/main',
  correlationId: 'correlation-$sequence',
  timestamp: DateTime.utc(2026, 7, 28, 8, 0, sequence),
  cards: cards,
  status: status,
);

Future<void> prepareShellSurface(WidgetTester tester) async {
  tester.view.physicalSize = const Size(1400, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

Future<void> drainShellTimers(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 400));
}

/// The preserved destinations are now deliberately behind Settings > OldUI.
Future<void> enterOldUi(WidgetTester tester) async {
  await tester.pump();
  final settings = find.byKey(const Key('home_settings'));
  if (settings.evaluate().isEmpty) return;
  await tester.tap(settings);
  await tester.pump();
  await tester.tap(find.byKey(const Key('settings_old_ui')));
  await tester.pump();
}
