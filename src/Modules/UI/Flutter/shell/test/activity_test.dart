import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  testWidgets('activity shows journal facts without message content', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    turns.add(shellTurn(1, true, 'private customer message'));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_activity')));
    await enterOldUi(tester);
    await tester.pumpAndSettle();

    expect(find.text('UserMessaged'), findsOneWidget);
    expect(find.text('sequence 001'), findsOneWidget);
    expect(find.text('command c1'), findsOneWidget);
    expect(find.text('private customer message'), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets('activity renders the authoritative signal name', (tester) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );
    turns.add(
      shellTurn(2, true, 'private payload', signal: 'ObservedCustomSynapse'),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_activity')));
    await enterOldUi(tester);
    await tester.pumpAndSettle();

    expect(find.text('ObservedCustomSynapse'), findsOneWidget);
    expect(find.text('UserMessaged'), findsNothing);
    expect(find.text('private payload'), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets('the shared event projection survives destination changes', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_activity')));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    turns.add(shellTurn(9, false, 'arrived while activity was open'));
    await enterOldUi(tester);
    await tester.pumpAndSettle();

    expect(find.text('Responded'), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_chat')));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    expect(find.text('arrived while activity was open'), findsOneWidget);
    await drainShellTimers(tester);
  });
}
