import 'package:digitalbrain_flutter_shell/chat/brain_graph_simulation.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_examples_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_graph_scene.dart';
import 'support/shell_test_support.dart';

void main() {
  testWidgets(
    'desktop embeds the shared kit chat to the left of the 3D modules',
    (tester) async {
      await prepareShellSurface(tester);
      final scene = FakeGraphScene();
      await tester.pumpWidget(
        MaterialApp(
          home: GraphExamplesScreen(
            chatName: 'main',
            turns: const [],
            sceneFactory: () => scene,
          ),
        ),
      );
      await tester.pump();
      expect(find.byType(KitChat), findsOneWidget);
      expect(
        tester.getRect(find.byKey(const Key('graph_chat_panel'))).right,
        lessThan(
          tester.getRect(find.byKey(const Key('graph_brain_panel'))).left,
        ),
      );
      expect(
        scene.nodes.where((node) => node.kind == GraphNodeKind.module),
        hasLength(3),
      );
      expect(
        scene.nodes.any(
          (node) => node.id == 'review-chat' || node.id == 'behaviors',
        ),
        isFalse,
      );
      expect(find.text('SIMULATION'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await drainShellTimers(tester);
      await tester.pumpWidget(const SizedBox());
      expect(scene.disposed, isTrue);
    },
  );

  testWidgets(
    'subscription playback adds a bound edge then removes it entirely',
    (tester) async {
      await prepareShellSurface(tester);
      final scene = FakeGraphScene();
      await tester.pumpWidget(
        MaterialApp(
          home: GraphExamplesScreen(
            chatName: 'main',
            turns: const [],
            sceneFactory: () => scene,
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('graph_example_subscription')));
      await tester.pump();
      bool bound() => scene.edges.any(
        (edge) => edge.id == BrainGraphSimulation.boundSynapse.id,
      );
      expect(bound(), isFalse);
      await tester.pump(const Duration(milliseconds: 3500));
      expect(bound(), isTrue);
      await tester.pump(const Duration(milliseconds: 3500));
      expect(scene.pulse?.fromId, 'timer');
      expect(scene.pulse?.toId, 'tick-observer');
      await tester.tap(find.byKey(const Key('graph_simulation_pause')));
      await tester.pump(const Duration(seconds: 5));
      expect(bound(), isTrue);
      expect(scene.pulse, isNull);
      expect(find.textContaining('3/5'), findsOneWidget);
      await tester.tap(find.byKey(const Key('graph_simulation_pause')));
      await tester.pump(const Duration(milliseconds: 3500));
      expect(bound(), isFalse);
      await tester.tap(find.byKey(const Key('graph_simulation_reset')));
      await tester.pump();
      expect(find.text('Play an example'), findsOneWidget);
      expect(bound(), isFalse);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'switching examples cancels the old route and disposal stops playback',
    (tester) async {
      final simulation = BrainGraphSimulation();
      simulation.play(BrainGraphExample.subscription);
      await tester.pump(const Duration(milliseconds: 3500));
      expect(simulation.current?.bound, isTrue);
      simulation.play(BrainGraphExample.conversation);
      expect(simulation.stepIndex, 0);
      expect(
        simulation.edges.any(
          (edge) => edge.id == BrainGraphSimulation.boundSynapse.id,
        ),
        isFalse,
      );
      final signature = simulation.pulse!.signature;
      simulation.togglePause();
      await tester.pump(const Duration(seconds: 10));
      expect(simulation.stepIndex, 0);
      simulation.togglePause();
      expect(simulation.pulse!.signature, isNot(signature));
      simulation.dispose();
      await tester.pump(const Duration(seconds: 10));
    },
  );

  testWidgets('compact graph layout remains usable without overflow', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(600, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: GraphExamplesScreen(
          chatName: 'main',
          turns: const [],
          sceneFactory: FakeGraphScene.new,
        ),
      ),
    );
    await tester.pump();
    expect(find.byType(KitChat), findsOneWidget);
    expect(tester.takeException(), isNull);
    await drainShellTimers(tester);
    await tester.pumpWidget(const SizedBox());
  });
}
