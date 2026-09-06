import 'dart:async';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_home_screen.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_app.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_chat_screen.dart';
import 'package:digitalbrain_flutter_shell/chat/activity_projection.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'support/shell_test_support.dart';

ExecutionActivity activity(String id, String target) => ExecutionActivity(
  id: id,
  correlationId: id,
  rootSignalId: 's$id',
  triggerName: 'UserMessaged',
  title: 'Research $target',
  status: 'running',
  commandId: 'c$id',
  startedAt: DateTime.utc(2026, 9, 6),
  updatedAt: DateTime.utc(2026, 9, 6),
  participantNeuronIds: ['source', target],
  events: [
    ExecutionActivityEvent(
      operationId: id,
      signalId: 's$id',
      sourceNeuronId: 'source',
      targetNeuronId: target,
      signalType: 'Note',
      phase: 'accepted',
      timestamp: DateTime.utc(2026, 9, 6),
    ),
  ],
);

BrainSnapshot graph() => BrainSnapshot(
  rootId: 'source',
  observedAt: DateTime.utc(2026, 9, 6),
  nodes: [
    for (final id in ['source', 'Orleans', 'Flutter'])
      BrainNeuron(
        id: id,
        type: 'behavior',
        name: id,
        label: id,
        module: 'Core',
      ),
  ],
  synapses: [
    for (final id in ['Orleans', 'Flutter'])
      BrainSynapse(
        id: 'edge$id',
        sourceId: 'source',
        targetId: id,
        signalType: 'Note',
        kind: 'Bound',
      ),
  ],
);

const scene = SurfaceComponent(
  kind: 'split',
  properties: {'graphFraction': '0.61'},
  children: [
    SurfaceComponent(
      kind: 'brain-graph',
      children: [
        SurfaceComponent(
          kind: 'activity-list',
          properties: {'placement': 'top-right'},
        ),
      ],
    ),
    SurfaceComponent(
      kind: 'chat',
      properties: {'name': 'main', 'voice': 'true'},
    ),
  ],
);

void main() {
  testWidgets('an older surface read cannot replace a newer scripted Home', (
    tester,
  ) async {
    await prepareShellSurface(tester);
    final older = Completer<KitSurfaceState?>();
    final update = Completer<SceneOpenedEvent>();
    var reads = 0;
    await tester.pumpWidget(
      BrainChatApp(
        chatName: 'main',
        surfaceEvents: Stream.fromFuture(update.future),
        onReadSurface: (_) {
          reads++;
          return reads == 1
              ? older.future
              : Future.value(
                  const KitSurfaceState(
                    scenes: [
                      KitSurfaceScene(
                        surfaceKey: 'home',
                        title: 'Latest',
                        root: scene,
                      ),
                    ],
                  ),
                );
        },
      ),
    );
    await tester.pump();
    update.complete(
      const SceneOpenedEvent(
        sequence: 0,
        sceneKey: 'home',
        title: 'Latest',
        commandId: 'new',
        shell: 'desk',
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_home')), findsOneWidget);
    older.complete(null);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('activity_home')), findsOneWidget);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });

  testWidgets(
    'selected durable results load independently and stale requests cannot replace selection',
    (tester) async {
      await prepareShellSurface(tester);
      final a = Completer<List<ChatTurnEvent>>(),
          b = Completer<List<ChatTurnEvent>>();
      ChatTurnEvent result(String id) => ChatTurnEvent(
        sequence: 1,
        eventId: 'event-$id',
        fromUser: false,
        text: 'Result $id',
        commandId: 'c$id',
        signal: 'Responded',
        neuronId: 'source',
        caller: 'source',
        correlationId: id,
        timestamp: DateTime.utc(2026),
      );
      await tester.pumpWidget(
        MaterialApp(
          theme: KitTheme.light(),
          home: KitThemeScope(
            child: Scaffold(
              body: GraphHomeScreen(
                chatName: 'main',
                turns: const [],
                surfaceRoot: scene,
                onReadBrain: () async => graph(),
                onWatchActivities: () => Stream.value([
                  activity('a', 'Orleans'),
                  activity('b', 'Flutter'),
                ]),
                onReadActivityResults: (id) => id == 'a' ? a.future : b.future,
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('execution_activity_a')));
      await tester.pump(const Duration(milliseconds: 250));
      await tester.tap(find.byKey(const ValueKey('execution_activity_b')));
      await tester.pump(const Duration(milliseconds: 250));
      a.complete([result('a')]);
      await tester.pump();
      expect(find.text('Result a'), findsNothing);
      b.complete([result('b')]);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 150));
      expect(find.text('Result b'), findsOneWidget);
      expect(find.text('Result a'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );
  test(
    'an unrelated running node does not mark selected completed work as running',
    () {
      final current = BrainSnapshot(
        rootId: 'source',
        observedAt: DateTime.utc(2026),
        nodes: const [
          BrainNeuron(
            id: 'source',
            type: 'assistant',
            name: 'source',
            label: 'Ino',
            module: 'AI',
            status: 'Running',
          ),
        ],
      );
      final completed = ExecutionActivity(
        id: 'done',
        correlationId: 'done',
        rootSignalId: 's',
        triggerName: 'Note',
        title: 'Done',
        status: 'completed',
        startedAt: DateTime.utc(2026),
        updatedAt: DateTime.utc(2026),
        participantNeuronIds: const ['source'],
        events: [
          ExecutionActivityEvent(
            operationId: 'op',
            signalId: 's',
            sourceNeuronId: 'source',
            signalType: 'Note',
            phase: 'completed',
            timestamp: DateTime.utc(2026),
          ),
        ],
      );
      expect(
        activityCanvas(current, [completed]).nodes.single.status,
        'Completed',
      );
    },
  );
  test(
    'direct execution evidence remains visible without a topology subscription',
    () {
      final empty = BrainSnapshot(
        rootId: 'source',
        observedAt: DateTime.utc(2026, 9, 6),
      );
      final scoped = activityCanvas(empty, [activity('direct', 'Orleans')]);
      expect(
        scoped.nodes.map((node) => node.id),
        containsAll(['source', 'Orleans']),
      );
      expect(scoped.synapses.single.kind, 'Observed');
      expect(scoped.synapses.single.canUnsubscribe, isFalse);
    },
  );
  testWidgets(
    'startup scene arriving later mounts Home and Settings retains OldUI',
    (tester) async {
      await prepareShellSurface(tester);
      final events = Completer<SceneOpenedEvent>();
      KitSurfaceState? surface;
      await tester.pumpWidget(
        BrainChatApp(
          chatName: 'main',
          surfaceEvents: Stream.fromFuture(events.future),
          onReadSurface: (_) async => surface,
        ),
      );
      await tester.pumpAndSettle();
      expect(
        find.text('Home has not been opened by the UI startup script.'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('destination_chat')), findsNothing);
      surface = const KitSurfaceState(
        scenes: [
          KitSurfaceScene(surfaceKey: 'home', title: 'My brain', root: scene),
        ],
      );
      events.complete(
        const SceneOpenedEvent(
          sequence: 1,
          sceneKey: 'home',
          title: 'My brain',
          commandId: 'startup',
          shell: 'desk',
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_home')), findsOneWidget);
      await tester.tap(find.byKey(const Key('home_settings')));
      await tester.pump();
      expect(find.byKey(const Key('settings_ui_kit')), findsOneWidget);
      expect(find.byKey(const Key('settings_old_ui')), findsOneWidget);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets(
    'changing activity keeps composer draft and hides other activity results',
    (tester) async {
      await prepareShellSurface(tester);
      var command = 'ca';
      final journal = [
        shellTurn(1, true, 'Orleans question', commandId: 'ca'),
        shellTurn(2, false, 'Orleans answer', commandId: 'ca'),
        shellTurn(3, true, 'Flutter question', commandId: 'cb'),
        shellTurn(4, false, 'Flutter answer', commandId: 'cb'),
      ];
      Future<void> show() => tester.pumpWidget(
        MaterialApp(
          theme: KitTheme.light(),
          home: KitThemeScope(
            child: Scaffold(
              body: BrainChatScreen(
                chatName: 'main',
                turns: journal,
                activityMode: true,
                activityCommandId: command,
                onSend: (_) async {},
              ),
            ),
          ),
        ),
      );
      await show();
      await tester.pumpAndSettle();
      expect(find.text('Orleans answer'), findsOneWidget);
      expect(find.text('Flutter answer'), findsNothing);
      await tester.enterText(find.byType(EditableText), 'Keep this draft');
      command = 'cb';
      await show();
      await tester.pumpAndSettle();
      expect(find.text('Flutter answer'), findsOneWidget);
      expect(find.text('Orleans answer'), findsNothing);
      expect(
        tester.widget<EditableText>(find.byType(EditableText)).controller.text,
        'Keep this draft',
      );
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );

  testWidgets('narrow Home stacks its script panels without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: KitTheme.light(),
        home: KitThemeScope(
          child: Scaffold(
            body: GraphHomeScreen(
              chatName: 'main',
              turns: const [],
              surfaceRoot: scene,
              onReadBrain: () async => graph(),
              onWatchActivities: () => Stream.value([activity('a', 'Orleans')]),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    await drainShellTimers(tester);
  });
  test(
    'activity scope hides unrelated topology and does not invent delivery edges',
    () {
      final scoped = activityCanvas(graph(), [activity('a', 'Orleans')]);
      expect(scoped.nodes.map((n) => n.id), ['source', 'Orleans']);
      expect(scoped.synapses.map((e) => e.id), ['edgeOrleans']);
    },
  );
  testWidgets(
    'script composed home selects activities and keeps task manager inside graph',
    (tester) async {
      await prepareShellSurface(tester);
      await tester.pumpWidget(
        MaterialApp(
          theme: KitTheme.light(),
          home: KitThemeScope(
            child: Scaffold(
              body: GraphHomeScreen(
                chatName: 'main',
                turns: const [],
                surfaceRoot: scene,
                onReadBrain: () async => graph(),
                onWatchActivities: () => Stream.value([
                  activity('a', 'Orleans'),
                  activity('b', 'Flutter'),
                ]),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('activity_task_manager')), findsOneWidget);
      expect(find.byKey(const Key('activity_split')), findsOneWidget);
      await tester.tap(find.byKey(const ValueKey('execution_activity_b')));
      await tester.pumpAndSettle();
      final canvas = tester.widget<LumenBrainGraph>(
        find.byType(LumenBrainGraph),
      );
      expect(canvas.snapshot.nodes.map((n) => n.id), contains('Flutter'));
      expect(
        canvas.snapshot.nodes.map((n) => n.id),
        isNot(contains('Orleans')),
      );
      await tester.tap(find.byKey(const Key('all_activities')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<LumenBrainGraph>(find.byType(LumenBrainGraph))
            .snapshot
            .nodes
            .length,
        3,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      await drainShellTimers(tester);
    },
  );
}
