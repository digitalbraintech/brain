import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_screen.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_home_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/shell_test_support.dart';

BrainSnapshot snapshot({bool subscribed = true}) => BrainSnapshot(
  rootId: 'chat:owner/main',
  observedAt: DateTime.utc(2026, 9, 5, 10),
  scope: 'Current conversation',
  nodes: const [
    BrainNeuron(
      id: 'timer',
      type: 'Timer',
      name: 'daily',
      label: 'Daily timer',
      module: 'Time',
      handledSignals: ['Tick'],
    ),
    BrainNeuron(
      id: 'assistant',
      type: 'Assistant',
      name: 'main',
      label: 'Ino',
      module: 'AI',
      handledSignals: ['Tick'],
    ),
  ],
  synapses: subscribed
      ? const [
          BrainSynapse(
            id: 'timer-assistant',
            sourceId: 'timer',
            targetId: 'assistant',
            signalType: 'Tick',
            kind: 'Bound',
            canUnsubscribe: true,
          ),
        ]
      : const [],
);

Widget host(Widget child) => MaterialApp(
  theme: KitTheme.light(),
  home: KitThemeScope(child: Scaffold(body: child)),
);

void main() {
  testWidgets('historical entries render separately without an invented time', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final history = BrainSnapshot(
      rootId: 'chat:main',
      observedAt: DateTime.utc(2026, 9, 5),
      nodes: const [
        BrainNeuron(
          id: 'assistant',
          type: 'assistant',
          name: 'assistant',
          label: 'Ino',
          module: 'AI',
        ),
      ],
      activity: const [
        BrainActivity(
          id: 'unknown:3',
          neuronId: 'assistant',
          direction: 'Incoming',
          sequence: 3,
          signalType: 'Unavailable history',
          timestamp: null,
          kind: 'historical',
          state: 'unavailable',
          summary:
              'Historical event unavailable because its type is no longer installed.',
        ),
      ],
    );
    await tester.pumpWidget(
      host(
        GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          onReadBrain: () async => history,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('neuron_assistant')));
    await tester.pumpAndSettle();
    expect(find.text('Unavailable historical event'), findsOneWidget);
    expect(find.text('Incoming journal · #3'), findsOneWidget);
    expect(find.textContaining(RegExp(r'\d\d:\d\d:\d\d · #3')), findsNothing);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });

  BrainSnapshot aspireSnapshot({bool completed = false, String? failureCode}) =>
      BrainSnapshot(
        rootId: 'chat:main',
        observedAt: DateTime.utc(2026, 9, 5, 12),
        nodes: [
          const BrainNeuron(
            id: 'assistant',
            type: 'assistant',
            name: 'assistant',
            label: 'Ino',
            module: 'AI',
          ),
          BrainNeuron(
            id: 'aspire',
            type: 'aspire',
            name: 'digitalbrain-local',
            label: 'Aspire',
            module: 'Microsoft',
            iconKey: 'aspire',
            status: failureCode != null
                ? 'Failed'
                : completed
                ? 'Idle'
                : 'Running',
          ),
        ],
        activity: [
          BrainActivity(
            id: 'request-start',
            neuronId: 'assistant',
            direction: 'Outgoing',
            sequence: 1,
            signalType: 'AgentActivity',
            timestamp: DateTime.utc(2026, 9, 5, 12),
            operationId: 'request-1',
            kind: 'delegation',
            state: completed ? 'completed' : 'started',
            name: 'Aspire',
            targetId: 'aspire',
          ),
          if (completed)
            BrainActivity(
              id: 'tool-result',
              neuronId: 'aspire',
              direction: 'Outgoing',
              sequence: 2,
              signalType: 'AgentActivity',
              timestamp: DateTime.utc(2026, 9, 5, 12),
              operationId: 'tool-1',
              kind: 'tool',
              state: failureCode != null ? 'failed' : 'completed',
              name: 'list_resources',
              server: 'Aspire',
              durationMs: 1234,
              resultPreview: '{"content":[{"type":"text","text":"Ready"}]}',
              failureCode: failureCode,
            ),
        ],
      );

  testWidgets(
    'first delegated request is inspectable before any synapse exists',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          MediaQuery(
            data: const MediaQueryData(disableAnimations: true),
            child: GraphHomeScreen(
              chatName: 'main',
              turns: const [],
              onReadBrain: () async => aspireSnapshot(),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final graph = tester.widget<LumenBrainGraph>(
        find.byType(LumenBrainGraph),
      );
      expect(graph.snapshot.synapses, isEmpty);
      expect(find.byKey(const ValueKey('neuron_aspire')), findsOneWidget);
      expect(
        tester
            .widget<CircularProgressIndicator>(
              find.byType(CircularProgressIndicator),
            )
            .value,
        1,
      );
      await tester.tap(find.byKey(const ValueKey('delegation_request-1')));
      await tester.pumpAndSettle();
      expect(find.text('Agent request'), findsOneWidget);
      expect(find.text('delegation · started'), findsOneWidget);
      expect(find.byKey(const Key('unsubscribe_synapse')), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'MCP inspector renders generic evidence without provider-specific cards',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => aspireSnapshot(completed: true),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('neuron_aspire')));
      await tester.pumpAndSettle();
      final tool = find.byKey(const ValueKey('activity_tool-result'));
      await tester.ensureVisible(tool);
      await tester.tap(tool);
      await tester.pumpAndSettle();
      expect(find.text('tool · completed · 1.2 s'), findsOneWidget);
      expect(
        find.text('{"content":[{"type":"text","text":"Ready"}]}'),
        findsOneWidget,
      );
      expect(find.text('MCP SERVER'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'stale graph suppresses working rings and temporary request traces',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          LumenBrainGraph(
            snapshot: aspireSnapshot(),
            stale: true,
            onNeuron: (_) {},
            onSynapse: (_) {},
            onActivity: (_) {},
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('delegation_request-1')), findsNothing);
      expect(find.byType(CircularProgressIndicator), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'failed operation inspector shows a useful safe failure category',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => aspireSnapshot(
              completed: true,
              failureCode: 'authentication_required',
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('neuron_aspire')));
      await tester.pumpAndSettle();
      final activity = find.byKey(const ValueKey('activity_tool-result'));
      await tester.ensureVisible(activity);
      await tester.tap(activity);
      await tester.pumpAndSettle();
      expect(find.text('FAILURE CATEGORY'), findsOneWidget);
      expect(find.text('Sign-in required'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'parallel, reverse and self synapses have distinct inspect controls',
    (tester) async {
      await prepareShellSurface(tester);
      final base = snapshot();
      final edges = [
        ...base.synapses,
        const BrainSynapse(
          id: 'second',
          sourceId: 'timer',
          targetId: 'assistant',
          signalType: 'Reminder',
          kind: 'Learned',
        ),
        const BrainSynapse(
          id: 'reverse',
          sourceId: 'assistant',
          targetId: 'timer',
          signalType: 'Response',
          kind: 'Bound',
        ),
        const BrainSynapse(
          id: 'self',
          sourceId: 'timer',
          targetId: 'timer',
          signalType: 'Tick',
          kind: 'Bound',
        ),
      ];
      final inspected = <String>[];
      await tester.pumpWidget(
        host(
          LumenBrainGraph(
            snapshot: BrainSnapshot(
              rootId: base.rootId,
              observedAt: base.observedAt,
              scope: base.scope,
              nodes: base.nodes,
              synapses: edges,
            ),
            onNeuron: (_) {},
            onSynapse: (edge) => inspected.add(edge.id),
          ),
        ),
      );
      await tester.pumpAndSettle();
      final rects = <Rect>[];
      for (final edge in edges) {
        final control = find.byKey(ValueKey('synapse_${edge.id}'));
        final rect = tester.getRect(control);
        expect(rects.every((previous) => !previous.overlaps(rect)), isTrue);
        rects.add(rect);
        await tester.tap(control);
        await tester.pumpAndSettle();
      }
      expect(inspected, edges.map((edge) => edge.id).toList());
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets('home shows observed graph above the compact conversation', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    await tester.pumpWidget(
      host(
        GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          onReadBrain: () async => snapshot(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byType(LumenBrainGraph), findsOneWidget);
    expect(
      tester.widget<BrainChatScreen>(find.byType(BrainChatScreen)).presentation,
      BrainChatPresentation.compact,
    );
    expect(
      tester.getRect(find.byKey(const Key('graph_brain_panel'))).bottom,
      lessThanOrEqualTo(
        tester.getRect(find.byKey(const Key('graph_chat_panel'))).top,
      ),
    );
    expect(find.text('SIMULATION'), findsNothing);
    expect(find.textContaining('2 neurons · 1 synapses'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });

  testWidgets(
    'inspect a real bound edge and confirm its removal from snapshot',
    (tester) async {
      await prepareShellSurface(tester);
      var subscribed = true;
      final changes = <bool>[];
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => snapshot(subscribed: subscribed),
            onSetBrainSubscription:
                ({
                  required sourceId,
                  required targetId,
                  required signalType,
                  required subscribed,
                }) async {
                  expect(sourceId, 'timer');
                  expect(targetId, 'assistant');
                  expect(signalType, 'Tick');
                  changes.add(subscribed);
                },
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('synapse_timer-assistant')));
      await tester.pumpAndSettle();
      expect(find.text('Bound'), findsOneWidget);
      subscribed = false;
      await tester.tap(find.byKey(const Key('unsubscribe_synapse')));
      await tester.pumpAndSettle();
      expect(changes, [false]);
      expect(
        tester
            .widget<LumenBrainGraph>(find.byType(LumenBrainGraph))
            .snapshot
            .synapses,
        isEmpty,
      );
      expect(
        find.text('This connection is no longer in the current graph.'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'unavailable observation is visible and never becomes simulated activity',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        host(
          GraphHomeScreen(
            chatName: 'main',
            turns: const [],
            onReadBrain: () async => throw StateError('offline'),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Observation unavailable'), findsOneWidget);
      expect(find.byType(LumenBrainGraph), findsNothing);
      expect(find.text('Retry'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets('narrow graph and directory remain usable without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      host(
        GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          onReadBrain: () async => snapshot(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('brain_directory')));
    await tester.pumpAndSettle();
    expect(find.text('Your neurons'), findsOneWidget);
    expect(find.text('Daily timer'), findsWidgets);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });
}
