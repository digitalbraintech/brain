import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

void main() {
  Future<void> openConversation(WidgetTester tester) async {
    await enterOldUi(tester);
    await tester.tap(find.byKey(const Key('destination_chat')));
    await tester.pump();
  }

  Future<void> send(WidgetTester tester, String text) async {
    await tester.enterText(find.byType(EditableText), text);
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pump();
  }

  testWidgets(
    'old replies and unrelated lifecycle do not erase current streams',
    (tester) async {
      await prepareShellSurface(tester);
      final replies = <StreamController<ChatDelta>>[];
      var journal = [
        shellTurn(1, true, 'previous', commandId: 'old'),
        shellTurn(2, false, 'previous answer', commandId: 'old'),
      ];
      Future<void> show() => tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BrainChatScreen(
              chatName: 'main',
              turns: journal,
              onStream: (_) {
                final reply = StreamController<ChatDelta>();
                addTearDown(reply.close);
                replies.add(reply);
                return reply.stream;
              },
            ),
          ),
        ),
      );
      await show();
      await tester.pumpAndSettle();
      await send(tester, 'next');
      await send(tester, 'another');
      replies[0].add(
        const ChatDelta.accepted(commandId: 'next', turnId: 't-next'),
      );
      replies[1].add(
        const ChatDelta.accepted(commandId: 'another', turnId: 't-another'),
      );
      await tester.pump();
      final controller = tester
          .widget<KitChat>(find.byType(KitChat))
          .chatController;
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(2));
      journal = [
        ...journal,
        shellTurn(3, true, 'next', commandId: 'next'),
        shellTurn(
          4,
          false,
          'Running',
          commandId: 'next',
          signal: 'TurnLifecycle',
          status: 'Running',
        ),
        shellTurn(5, true, 'another', commandId: 'another'),
        shellTurn(6, false, 'background note', commandId: 'unrelated'),
      ];
      await show();
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(2));
      expect(
        controller.messages.whereType<TextMessage>().where(
          (m) => m.text == 'next',
        ),
        hasLength(1),
      );
      expect(
        controller.messages.whereType<TextMessage>().where(
          (m) => m.text == 'Running',
        ),
        isEmpty,
      );
      journal = [
        ...journal,
        shellTurn(7, false, 'next answer', commandId: 'next'),
      ];
      await show();
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(1));
      journal = [
        ...journal,
        shellTurn(
          8,
          false,
          'Internal storage exception with long stack trace',
          commandId: 'another',
          signal: 'TurnLifecycle',
          status: 'Failed',
        ),
      ];
      await show();
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), isEmpty);
      expect(
        controller.messages.whereType<TextMessage>().map((m) => m.text),
        contains('Request failed. See Activity for details.'),
      );
      expect(
        controller.messages.whereType<TextMessage>().map((m) => m.text).join(),
        isNot(contains('Internal storage exception')),
      );
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
      await tester.pump();
      expect(tester.takeException(), isNull);
    },
    timeout: const Timeout(Duration(seconds: 30)),
  );

  testWidgets(
    'empty reply is visible and unmount cancels an active subscription',
    (tester) async {
      await prepareShellSurface(tester);
      var cancelled = false;
      final replies = StreamController<ChatDelta>(
        onCancel: () {
          cancelled = true;
        },
      );
      addTearDown(replies.close);
      var useEmpty = true;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BrainChatScreen(
              chatName: 'main',
              turns: const [],
              onStream: (_) =>
                  useEmpty ? const Stream<ChatDelta>.empty() : replies.stream,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await send(tester, 'empty');
      await tester.pump(const Duration(milliseconds: 100));
      expect(find.textContaining('without a response'), findsWidgets);
      useEmpty = false;
      await send(tester, 'still waiting');
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
      await tester.pump();
      expect(cancelled, isTrue);
      replies.add(const ChatDelta(role: 'assistant', contents: []));
      await tester.pump();
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'late initial history and identical prompts use accepted command IDs',
    (tester) async {
      await prepareShellSurface(tester);
      final replies = <StreamController<ChatDelta>>[];
      var journal = <ChatTurnEvent>[];
      Future<void> show() => tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BrainChatScreen(
              chatName: 'main',
              turns: journal,
              onStream: (_) {
                final reply = StreamController<ChatDelta>();
                addTearDown(reply.close);
                replies.add(reply);
                return reply.stream;
              },
            ),
          ),
        ),
      );
      await show();
      await tester.pumpAndSettle();
      await send(tester, 'same');
      await send(tester, 'same');
      final controller = tester
          .widget<KitChat>(find.byType(KitChat))
          .chatController;
      journal = [
        shellTurn(1, true, 'same', commandId: 'old'),
        shellTurn(2, false, 'old reply', commandId: 'old'),
      ];
      await show();
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(2));
      expect(
        controller.messages.whereType<TextMessage>().where(
          (m) => m.text == 'same',
        ),
        hasLength(3),
      );
      journal = [
        ...journal,
        shellTurn(3, true, 'same', commandId: 'second'),
        shellTurn(4, false, 'second reply', commandId: 'second'),
      ];
      await show();
      await tester.pump();
      replies[1].add(
        const ChatDelta.accepted(commandId: 'second', turnId: 't-second'),
      );
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(1));
      replies[0].add(
        const ChatDelta.accepted(commandId: 'first', turnId: 't-first'),
      );
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), hasLength(1));
      journal = [
        ...journal,
        shellTurn(5, true, 'same', commandId: 'first'),
        shellTurn(6, false, 'first reply', commandId: 'first'),
      ];
      await show();
      await tester.pump();
      expect(controller.messages.whereType<TextStreamMessage>(), isEmpty);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
      await tester.pump();
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('journal recovery clears only the matching send failure', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final replies = <StreamController<ChatDelta>>[];
    var journal = <ChatTurnEvent>[];
    Future<void> show() => tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BrainChatScreen(
            chatName: 'main',
            turns: journal,
            onStream: (_) {
              final reply = StreamController<ChatDelta>();
              addTearDown(reply.close);
              replies.add(reply);
              return reply.stream;
            },
          ),
        ),
      ),
    );
    await show();
    await tester.pumpAndSettle();
    await send(tester, 'first');
    await send(tester, 'second');
    for (var index = 0; index < replies.length; index++) {
      replies[index].add(
        ChatDelta.accepted(commandId: 'c$index', turnId: 't$index'),
      );
      await tester.pump();
      replies[index].addError(StateError('temporary connection failure'));
      await tester.pump();
    }
    expect(find.textContaining('temporary connection failure'), findsWidgets);
    journal = [
      shellTurn(1, true, 'first', commandId: 'c0'),
      shellTurn(2, false, 'first recovered', commandId: 'c0'),
    ];
    await show();
    await tester.pump();
    expect(find.textContaining('temporary connection failure'), findsWidgets);
    journal = [
      ...journal,
      shellTurn(3, true, 'second', commandId: 'c1'),
      shellTurn(4, false, 'second recovered', commandId: 'c1'),
    ];
    await show();
    await tester.pump();
    expect(find.textContaining('temporary connection failure'), findsNothing);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
    await tester.pump();
    expect(tester.takeException(), isNull);
  });

  testWidgets('journal turns project as message text on the chat surface', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );
    await openConversation(tester);

    turns.add(shellTurn(1, true, 'how is my account?'));
    turns.add(shellTurn(2, false, 'Your account is up to date.'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('chat_surface')), findsOneWidget);
    expect(find.text('how is my account?'), findsOneWidget);
    expect(find.text('Your account is up to date.'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets('a repeated sequence is projected once', (tester) async {
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    await tester.pumpWidget(
      BrainChatApp(chatName: 'main', turns: turns.stream, onSend: (_) async {}),
    );
    await openConversation(tester);

    turns.add(shellTurn(7, false, 'only once'));
    turns.add(shellTurn(7, false, 'only once'));
    await tester.pumpAndSettle();

    expect(find.text('only once'), findsOneWidget);
    await drainShellTimers(tester);
  });

  testWidgets(
    'sending hands the text to the edge and shows the journal answer',
    (tester) async {
      await prepareShellSurface(tester);
      final sent = <String>[];
      final turns = StreamController<ChatTurnEvent>();
      addTearDown(turns.close);

      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          turns: turns.stream,
          onSend: (text) async => sent.add(text),
        ),
      );
      await openConversation(tester);

      await tester.enterText(find.byType(EditableText), 'enrich my account');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pumpAndSettle();

      expect(sent, ['enrich my account']);
      expect(find.text('enrich my account'), findsWidgets);

      turns.add(shellTurn(1, true, 'enrich my account'));
      turns.add(shellTurn(2, false, 'Done.'));
      await tester.pumpAndSettle();

      expect(find.text('Done.'), findsOneWidget);
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'workspace navigation does not wipe an optimistic send before journal arrives',
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
      await openConversation(tester);

      await tester.enterText(find.byType(EditableText), 'stay visible');
      await tester.testTextInput.receiveAction(TextInputAction.send);
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsWidgets);

      await tester.tap(find.byKey(const Key('destination_activity')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('destination_chat')));
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsWidgets);

      turns.add(shellTurn(1, true, 'stay visible'));
      turns.add(shellTurn(2, false, 'still here'));
      await tester.pumpAndSettle();

      expect(find.text('stay visible'), findsOneWidget);
      expect(find.text('still here'), findsOneWidget);
      await drainShellTimers(tester);
    },
  );

  testWidgets('an in-flight stream bubble survives journal user-turn arrival', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final turns = StreamController<ChatTurnEvent>();
    addTearDown(turns.close);

    // Completes after a short delay so the bubble exists while the user
    // journal lands, without leaving an open stream that hangs the test.
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        turns: turns.stream,
        onStream: (_) async* {
          yield const ChatDelta.accepted(commandId: 'c1', turnId: 't1');
          await Future<void>.delayed(const Duration(milliseconds: 80));
        },
      ),
    );
    await openConversation(tester);

    await tester.enterText(find.byType(EditableText), 'stream me');
    await tester.testTextInput.receiveAction(TextInputAction.send);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(find.text('stream me'), findsWidgets);

    turns.add(shellTurn(1, true, 'stream me'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 20));

    expect(find.text('stream me'), findsWidgets);

    await tester.pump(const Duration(milliseconds: 100));
    turns.add(shellTurn(2, false, 'stream done', commandId: 'c1'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('stream me'), findsOneWidget);
    expect(find.text('stream done'), findsOneWidget);
    await drainShellTimers(tester);
  });
}
