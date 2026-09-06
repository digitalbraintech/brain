import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';
import 'support/fake_graph_scene.dart';

void main() {
  testWidgets('OldUI starts in My brain and exposes its destinations', (
    tester,
  ) async {
    await prepareShellSurface(tester);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', graphSceneFactory: FakeGraphScene.new),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await drainShellTimers(tester);

    expect(find.byKey(const Key('destination_chat')), findsOneWidget);
    expect(find.byKey(const Key('destination_onboarding')), findsOneWidget);
    expect(find.byKey(const Key('destination_graph')), findsOneWidget);
    expect(find.byKey(const Key('destination_activity')), findsOneWidget);
    expect(find.byKey(const Key('destination_kit')), findsOneWidget);
    expect(find.byKey(const Key('destination_windowing')), findsOneWidget);
    expect(find.byKey(const Key('compact_chat_surface')), findsOneWidget);
    expect(find.text('A little more headspace.'), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_onboarding')));
    await enterOldUi(tester);
    await tester.pump();
    await drainShellTimers(tester);
    expect(find.byKey(const Key('onboarding_screen')), findsOneWidget);
    expect(find.byKey(const Key('onboarding_capability_rail')), findsOneWidget);
    expect(find.byKey(const Key('kit_graph')), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_graph')));
    await enterOldUi(tester);
    await tester.pump();
    expect(find.byKey(const Key('graph_home_screen')), findsOneWidget);
    expect(find.byKey(const Key('graph_brain_panel')), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_activity')));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_screen')), findsOneWidget);
    expect(find.text('No activity yet.'), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_kit')));
    await enterOldUi(tester);
    await tester.pump();
    await drainShellTimers(tester);
    expect(find.byKey(const Key('kit_gallery_screen')), findsOneWidget);
    expect(find.text('UI Kit'), findsOneWidget);

    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_windowing')));
    await enterOldUi(tester);
    await tester.pump();
    await drainShellTimers(tester);
    expect(find.byKey(const Key('windowing_screen')), findsOneWidget);
    expect(find.textContaining('Windowing demo'), findsOneWidget);
    expect(find.text('BTC / USD'), findsWidgets);
    expect(find.byKey(const Key('kit_time_chart')), findsOneWidget);
  });

  testWidgets('assistant hint requests local code review through chat', (
    tester,
  ) async {
    String? sent;
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', onSend: (text) async => sent = text),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(const Key('assistant_hint_personal_code_review')),
    );
    await enterOldUi(tester);
    await tester.pump();
    expect(
      sent,
      'Review my local repository diff. Focus on correctness, concurrency, and durable state. '
      'Give actionable findings with file and line references; skip cosmetic comments.',
    );
    await drainShellTimers(tester);
  });

  testWidgets('narrow windows use bottom navigation', (tester) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(const BrainChatApp(chatName: 'main'));
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await drainShellTimers(tester);

    expect(find.byKey(const Key('destination_graph')), findsOneWidget);
    expect(find.byKey(const Key('destination_chat')), findsOneWidget);
    expect(find.byTooltip('More destinations'), findsOneWidget);
    expect(find.byKey(const Key('destination_onboarding')), findsNothing);
    await drainShellTimers(tester);
  });

  testWidgets(
    'a disconnected edge says so and mounts chat without a send path',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        const BrainChatApp(chatName: 'main', statusMessage: 'no edge'),
      );
      await enterOldUi(tester);
      await tester.pump();

      expect(find.text('not connected'), findsOneWidget);
      expect(find.byKey(const Key('compact_chat_surface')), findsOneWidget);
      await drainShellTimers(tester);
    },
  );

  testWidgets('My brain and Conversation preserve a pending send and draft', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    var subscriptions = 0;
    var cancellations = 0;
    final replies = StreamController<ChatDelta>(
      onListen: () => subscriptions++,
      onCancel: () => cancellations++,
    );
    addTearDown(() async {
      if (subscriptions == 0) await replies.stream.listen(null).cancel();
      await replies.close();
    });
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);
    final sent = <String>[];
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onStream: (text) {
          sent.add(text);
          return replies.stream;
        },
      ),
    );
    await enterOldUi(tester);
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(EditableText), 'Review my diff');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await enterOldUi(tester);
    await tester.pump();
    replies.add(
      const ChatDelta.accepted(commandId: 'review', turnId: 't-review'),
    );
    await enterOldUi(tester);
    await tester.pump();
    await tester.enterText(find.byType(EditableText), 'A follow-up draft');
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_chat')));
    await enterOldUi(tester);
    await tester.pump();
    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    expect(find.text('Review my diff'), findsOneWidget);
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'A follow-up draft',
    );
    expect(subscriptions, 1);
    expect(cancellations, 0);
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_graph')));
    await enterOldUi(tester);
    await tester.pump();
    expect(find.byKey(const Key('compact_chat_surface')), findsOneWidget);
    expect(
      tester.widget<EditableText>(find.byType(EditableText)).controller.text,
      'A follow-up draft',
    );
    turns.add(shellTurn(1, true, 'Review my diff', commandId: 'review'));
    turns.add(
      shellTurn(2, false, 'One actionable finding.', commandId: 'review'),
    );
    await enterOldUi(tester);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    expect(
      tester.widget<KitMarkdown>(find.byType(KitMarkdown)).text,
      'One actionable finding.',
    );
    expect(sent, ['Review my diff']);
    expect(subscriptions, 1);
    expect(cancellations, 0);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
    expect(cancellations, 1);
    expect(tester.takeException(), isNull);
  }, timeout: const Timeout(Duration(seconds: 60)));
}
