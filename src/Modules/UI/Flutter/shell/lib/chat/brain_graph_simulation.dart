import 'dart:async';

import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/foundation.dart';

enum BrainGraphExample {
  conversation(
    'Chat reply',
    'A message travels through chat and the assistant.',
  ),
  subscription(
    'Subscribe / unsubscribe',
    'Bind a subscriber, deliver a signal, then remove the synapse.',
  );

  const BrainGraphExample(this.label, this.description);
  final String label;
  final String description;
}

final class BrainGraphStep {
  const BrainGraphStep(
    this.title,
    this.detail, {
    this.from,
    this.to,
    this.bound = false,
  });

  final String title;
  final String detail;
  final String? from;
  final String? to;
  final bool bound;
}

/// A local, illustrative playback. It does not submit commands or claim to
/// project runtime synapses. Module envelopes are containment, never edges.
final class BrainGraphSimulation extends ChangeNotifier {
  BrainGraphSimulation({
    this.stepDuration = const Duration(milliseconds: 3500),
  });

  final Duration stepDuration;
  Timer? _timer;
  BrainGraphExample? _example;
  int _step = -1;
  int _run = 0;
  bool _playing = false;

  BrainGraphExample? get example => _example;
  bool get playing => _playing;
  int get stepIndex => _step;
  List<BrainGraphStep> get steps => switch (_example) {
    BrainGraphExample.conversation => _conversation,
    BrainGraphExample.subscription => _subscription,
    null => const [],
  };
  BrainGraphStep? get current => _step < 0 ? null : steps[_step];
  bool get complete => !playing && _step >= 0 && _step == steps.length - 1;

  List<GraphEdge> get edges => [
    ..._learnedSynapses,
    if (current?.bound == true) boundSynapse,
  ];

  GraphPulse? get pulse {
    final step = current;
    if (!playing || step?.from == null || step?.to == null) return null;
    return GraphPulse(
      fromId: step!.from!,
      toId: step.to!,
      signature: '$_run:$_step',
    );
  }

  void play(BrainGraphExample example) {
    _timer?.cancel();
    _example = example;
    _step = 0;
    _run++;
    _playing = true;
    _schedule();
    notifyListeners();
  }

  void togglePause() {
    if (_example == null) return;
    if (complete) {
      play(_example!);
      return;
    }
    _playing = !_playing;
    _timer?.cancel();
    if (_playing) {
      _run++;
      _schedule();
    }
    notifyListeners();
  }

  void _schedule() => _timer = Timer.periodic(stepDuration, (_) {
    if (_step < steps.length - 1) {
      _step++;
    } else {
      _playing = false;
      _timer?.cancel();
    }
    notifyListeners();
  });

  void reset() {
    _timer?.cancel();
    _example = null;
    _step = -1;
    _playing = false;
    notifyListeners();
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  static const nodes = [
    GraphNode(
      id: 'ui-module',
      label: 'UI',
      kind: GraphNodeKind.module,
      cluster: 'UI',
      position: GraphPoint(-0.60, 0.46, 0.02),
    ),
    GraphNode(
      id: 'chat',
      label: 'Chat',
      cluster: 'UI',
      position: GraphPoint(-0.78, 0.58, 0.04),
    ),
    GraphNode(
      id: 'turn-worker',
      label: 'Turn worker',
      cluster: 'UI',
      position: GraphPoint(-0.45, 0.49, -0.05),
    ),
    GraphNode(
      id: 'ai-module',
      label: 'AI',
      kind: GraphNodeKind.module,
      cluster: 'AI',
      position: GraphPoint(0.42, 0.47, -0.13),
    ),
    GraphNode(
      id: 'assistant',
      label: 'Assistant',
      cluster: 'AI',
      position: GraphPoint(0.40, 0.45, 0.04),
    ),
    GraphNode(
      id: 'time-module',
      label: 'Time',
      kind: GraphNodeKind.module,
      cluster: 'Time',
      position: GraphPoint(0.55, -0.43, 0.10),
    ),
    GraphNode(
      id: 'timer',
      label: 'Timer',
      cluster: 'Time',
      position: GraphPoint(0.39, -0.34, 0.08),
    ),
    GraphNode(
      id: 'tick-observer',
      label: 'Tick observer · example',
      cluster: 'Time',
      position: GraphPoint(0.62, -0.60, 0.16),
    ),
  ];

  static const boundSynapse = GraphEdge(
    id: 'timer-tick-observer',
    sourceId: 'timer',
    targetId: 'tick-observer',
    decorated: true,
  );

  static const _learnedSynapses = [
    GraphEdge(id: 'chat-turn', sourceId: 'chat', targetId: 'turn-worker'),
    GraphEdge(
      id: 'turn-assistant',
      sourceId: 'turn-worker',
      targetId: 'assistant',
    ),
    GraphEdge(id: 'turn-chat', sourceId: 'turn-worker', targetId: 'chat'),
  ];

  static const _conversation = [
    BrainGraphStep(
      'Chat accepts the message',
      'The Chat neuron records the user message and schedules its turn worker.',
      from: 'chat',
      to: 'turn-worker',
    ),
    BrainGraphStep(
      'The turn worker asks the assistant',
      'The Assistant neuron runs the model and its tools for this conversation.',
      from: 'turn-worker',
      to: 'assistant',
    ),
    BrainGraphStep(
      'The reply returns to chat',
      'The turn worker records the completed answer in Chat. The UI reads the journal.',
      from: 'turn-worker',
      to: 'chat',
    ),
    BrainGraphStep(
      'Conversation complete',
      'Handled sends establish learned synapses on their source neurons.',
    ),
  ];

  static const _subscription = [
    BrainGraphStep(
      'Subscribe to Tick',
      'A sample Tick observer asks to subscribe to Timer. No delivery edge exists yet.',
    ),
    BrainGraphStep(
      'Timer binds its outgoing synapse',
      'SubscribeTo leads to BindOutgoing on the source. Timer now owns a Bound synapse to the sample observer.',
      bound: true,
    ),
    BrainGraphStep(
      'Timer broadcasts Tick',
      'Broadcast follows the existing Bound synapse to a neuron that handles Tick.',
      from: 'timer',
      to: 'tick-observer',
      bound: true,
    ),
    BrainGraphStep(
      'Unsubscribe removes the synapse',
      'The source removes this subscription entirely; no learned edge is left behind.',
    ),
    BrainGraphStep(
      'Next Tick has no subscriber',
      'Without that outgoing synapse, broadcast does not deliver to the sample observer.',
    ),
  ];
}
