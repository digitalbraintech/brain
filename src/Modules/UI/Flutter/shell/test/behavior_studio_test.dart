import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/behavior_studio.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_home_screen.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('new behavior starts with the standard SDK connection and input', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = FakeStudio()
      ..current = const SavedBehavior(
        id: 'behavior:new',
        name: 'new',
        enabled: false,
      );
    final changes = ChangeNotifier();
    addTearDown(changes.dispose);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorEditor(api: api, name: 'new', changes: changes),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save draft'));
    await tester.pumpAndSettle();
    expect(
      api.savedSource,
      'await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);\n'
      'return digitalBrain.Input<Note>();',
    );
    expect(api.current.draft!.inputSignalTypes, ['Note']);
    expect(api.current.draft!.outputSignalTypes, ['Note']);
  });

  testWidgets(
    'whiteboard keeps the existing camera and neuron position as the graph grows',
    (tester) async {
      BrainSnapshot snapshot(int count) => BrainSnapshot(
        rootId: 'chat:main',
        observedAt: DateTime.utc(2026),
        nodes: [
          const BrainNeuron(
            id: 'assistant:ino',
            type: 'assistant',
            name: 'ino',
            label: 'Ino',
            module: 'AI',
          ),
          for (var index = 0; index < count; index++)
            BrainNeuron(
              id: 'behavior:$index',
              type: 'behavior',
              name: '$index',
              label: '$index',
              module: 'Behaviors',
            ),
        ],
      );
      Widget graph(int count) => MaterialApp(
        home: Scaffold(
          body: LumenBrainGraph(
            snapshot: snapshot(count),
            onNeuron: (_) {},
            onSynapse: (_) {},
          ),
        ),
      );
      await tester.pumpWidget(graph(0));
      await tester.pumpAndSettle();
      final ino = find.byKey(const ValueKey('neuron_assistant:ino'));
      final initial = tester.getCenter(ino);
      await tester.pumpWidget(graph(30));
      await tester.pumpAndSettle();
      expect(tester.getCenter(ino), initial);
      expect(find.byKey(const ValueKey('neuron_behavior:29')), findsOneWidget);
    },
  );

  test('whiteboard starts with Ino and reveals real involved behaviors', () {
    final initial = BrainSnapshot(
      rootId: 'chat:main',
      observedAt: DateTime.utc(2026),
      nodes: const [
        BrainNeuron(
          id: 'assistant:assistant',
          type: 'assistant',
          name: 'Ino',
          label: 'Ino',
          module: 'AI',
        ),
        BrainNeuron(
          id: 'chat:main',
          type: 'chat',
          name: 'main',
          label: 'Conversation',
          module: 'UI',
        ),
        BrainNeuron(
          id: 'sessionneuron:session',
          type: 'sessionneuron',
          name: 'session',
          label: 'Session',
          module: 'Kernel',
          isInfrastructure: true,
        ),
        BrainNeuron(
          id: 'behavior:draft',
          type: 'behavior',
          name: 'draft',
          label: 'draft',
          module: 'Behaviors',
          role: 'library',
        ),
      ],
    );
    expect(studioCanvas(initial).nodes.map((node) => node.id), [
      'assistant:assistant',
    ]);
    expect(studioCanvas(initial, technical: true).nodes, hasLength(4));
    final involved = BrainSnapshot(
      rootId: initial.rootId,
      observedAt: initial.observedAt,
      nodes: [
        ...initial.nodes,
        const BrainNeuron(
          id: 'behavior:echo',
          type: 'behavior',
          name: 'echo',
          label: 'echo',
          module: 'Behaviors',
          status: 'Active',
        ),
      ],
      synapses: const [
        BrainSynapse(
          id: 'real',
          sourceId: 'behavior:echo',
          targetId: 'chat:main',
          signalType: 'Note',
          kind: 'Bound',
        ),
      ],
    );
    final canvas = studioCanvas(involved);
    expect(
      canvas.nodes.map((node) => node.id),
      containsAll(['assistant:assistant', 'behavior:echo', 'chat:main']),
    );
    expect(canvas.synapses.single.id, 'real');
  });

  testWidgets(
    'editor preserves active revision while saving a new draft with optimistic concurrency',
    (tester) async {
      tester.view.physicalSize = const Size(1200, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final api = FakeStudio();
      final changes = ChangeNotifier();
      addTearDown(changes.dispose);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BehaviorEditor(api: api, name: 'echo', changes: changes),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('behavior_source')),
        'return new Note("changed");',
      );
      await tester.tap(find.text('Save draft'));
      await tester.pumpAndSettle();
      expect(api.expected, '11111111-1111-1111-1111-111111111111');
      expect(api.savedSource, 'return new Note("changed");');
      expect(find.textContaining('Pending'), findsWidgets);
      expect(find.text('Currently active C#'), findsOneWidget);
      expect(api.current.active!.source, 'return Input;');
      expect(api.current.draft!.inputPolicy, 7);
    },
  );

  testWidgets('compilation progress updates without overwriting newer edits', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final api = FakeStudio();
    final changes = ChangeNotifier();
    addTearDown(changes.dispose);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: BehaviorEditor(api: api, name: 'echo', changes: changes),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save draft'));
    await tester.pumpAndSettle();
    expect(find.text('Draft saved; awaiting compilation.'), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('behavior_source')),
      'return new Note("new unsaved edit");',
    );
    final draft = api.current.draft!;
    api.current = SavedBehavior(
      id: api.current.id,
      name: api.current.name,
      enabled: true,
      active: FakeStudio.active,
      draft: BehaviorProgram(
        revision: draft.revision,
        source: draft.source,
        validation: 'Valid',
        inputSignalTypes: draft.inputSignalTypes,
        outputSignalTypes: draft.outputSignalTypes,
        inputPolicy: draft.inputPolicy,
      ),
      detail: 'Compiled revision ready.',
    );
    changes.notifyListeners();
    await tester.pumpAndSettle();
    expect(find.text('Draft saved; awaiting compilation.'), findsNothing);
    expect(find.text('Compiled revision ready.'), findsOneWidget);
    expect(
      tester
          .widget<TextField>(find.byKey(const Key('behavior_source')))
          .controller!
          .text,
      'return new Note("new unsaved edit");',
    );
  });
}

class FakeStudio implements BehaviorStudioApi {
  static const active = BehaviorProgram(
    revision: '11111111-1111-1111-1111-111111111111',
    source: 'return Input;',
    validation: 'Valid',
    inputSignalTypes: ['Note'],
    outputSignalTypes: ['Note'],
    inputPolicy: 7,
  );
  SavedBehavior current = const SavedBehavior(
    id: 'behavior:echo',
    name: 'echo',
    enabled: true,
    active: active,
    draft: active,
  );
  String? expected, savedSource;
  @override
  Future<List<SavedBehavior>> listBehaviors() async => [current];
  @override
  Future<SavedBehavior> readBehavior(String name) async => current;
  @override
  Future<SavedBehavior> saveBehavior(
    String name, {
    required String source,
    required List<String> inputSignalTypes,
    required List<String> outputSignalTypes,
    String? expectedDraftRevision,
    int? inputPolicy,
  }) async {
    expected = expectedDraftRevision;
    savedSource = source;
    return current = SavedBehavior(
      id: current.id,
      name: name,
      enabled: true,
      active: active,
      detail: 'Draft saved; awaiting compilation.',
      draft: BehaviorProgram(
        revision: '22222222-2222-2222-2222-222222222222',
        source: source,
        validation: 'Pending',
        inputSignalTypes: inputSignalTypes,
        outputSignalTypes: outputSignalTypes,
        inputPolicy: inputPolicy ?? active.inputPolicy,
      ),
    );
  }

  @override
  Future<SavedBehavior> enableBehavior(
    String name, {
    String? expectedDraftRevision,
  }) async => current;
  @override
  Future<SavedBehavior> disableBehavior(String name) async => current;
  @override
  Future<SavedBehavior> invokeBehavior(
    String name, {
    required String inputType,
    required Map<String, dynamic> input,
  }) async => current;
}
