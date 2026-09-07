import 'package:flutter/material.dart';

import '../components/graph/graph_models.dart';
import 'onboarding_models.dart';

abstract final class OnboardingCatalog {
  static const source = GraphNode(id: 'source', label: 'source');
  static const consumer = GraphNode(id: 'consumer', label: 'consumer');
  static const otherSource = GraphNode(
    id: 'other-source',
    label: 'other-source',
  );
  static const otherConsumer = GraphNode(
    id: 'other-consumer',
    label: 'other-consumer',
  );
  static const timeline = GraphNode(
    id: 'timeline',
    label: 'unsubscribed',
    dimmed: true,
  );
  static const profile = GraphNode(
    id: 'profile',
    label: 'profile',
    kind: GraphNodeKind.entity,
  );
  static const timeModule = GraphNode(
    id: 'time-module',
    label: 'Time',
    kind: GraphNodeKind.module,
  );
  static const timer = GraphNode(id: 'timer', label: 'Timer', cluster: 'Time');

  static const sourceToConsumer = GraphEdge(
    id: 'source-consumer',
    sourceId: 'source',
    targetId: 'consumer',
  );
  static const otherSourceToConsumer = GraphEdge(
    id: 'other-source-consumer',
    sourceId: 'other-source',
    targetId: 'other-consumer',
  );
  static const clientToTimer = GraphEdge(
    id: 'client-timer',
    sourceId: 'client',
    targetId: 'timer',
  );

  static const client = GraphNode(id: 'client', label: 'script');

  static List<OnboardingCapability> get capabilities => [
    fire,
    handle,
    synapse,
    broadcast,
    subscribe,
    journal,
    entity,
    module,
  ];

  static OnboardingCapability byId(String id) =>
      capabilities.firstWhere((capability) => capability.id == id);

  static const fire = OnboardingCapability(
    id: 'fire',
    title: 'Fire',
    blurb: 'Send a signal at a named neuron.',
    rule:
        'A script or assistant fires SendAsync at a named neuron. The payload is the Signal. The envelope (id, correlation, caller, sequence) rides with it. This is not a broadcast.',
    icon: Icons.flash_on_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'fire-1',
        ),
      ),
    ],
  );

  static const handle = OnboardingCapability(
    id: 'handle',
    title: 'Handle',
    blurb: 'IHandle is a type capability, not a subscription.',
    rule:
        'IHandle<ActivityChanged> means this grain type can receive ActivityChanged. It does not subscribe the handler to every source. Unhandled fires still journal; they do not learn a route.',
    icon: Icons.pan_tool_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'handle-1',
        ),
      ),
    ],
  );

  static const synapse = OnboardingCapability(
    id: 'synapse',
    title: 'Synapse',
    blurb: 'A handled fire writes an edge on the source.',
    rule:
        'A handled directed ActivityChanged delivery records an observational learned edge on its source. Unhandled fire: journal only, no edge.',
    icon: Icons.hub_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'syn-1',
        ),
      ),
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [sourceToConsumer],
        highlightEdgeId: 'source-consumer',
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'syn-2',
        ),
      ),
    ],
  );

  static const broadcast = OnboardingCapability(
    id: 'broadcast',
    title: 'Broadcast',
    blurb: 'Fan-out from this neuron’s audience.',
    rule:
        'Broadcast follows this source neuron’s existing synapses for the signal type. An unsubscribed handler receives nothing. The emitter never receives its own broadcast.',
    icon: Icons.campaign_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer, timeline],
        edges: [sourceToConsumer],
        duration: Duration(milliseconds: 500),
      ),
      OnboardingLessonFrame(
        nodes: [source, consumer, timeline],
        edges: [sourceToConsumer],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'broadcast-consumer',
        ),
        highlightEdgeId: 'source-consumer',
      ),
      OnboardingLessonFrame(
        nodes: [source, consumer, timeline],
        edges: [sourceToConsumer],
      ),
    ],
  );

  static const subscribe = OnboardingCapability(
    id: 'subscribe',
    title: 'Subscribe',
    blurb: 'A subscription names one exact source.',
    rule:
        'Each subscription is a bound edge on one named source. A broadcast follows only those explicit bound edges for its signal type.',
    icon: Icons.notifications_active_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer, otherSource, otherConsumer],
        edges: [sourceToConsumer, otherSourceToConsumer],
        duration: Duration(milliseconds: 700),
      ),
      OnboardingLessonFrame(
        nodes: [
          source,
          consumer,
          otherSource,
          GraphNode(
            id: 'other-consumer',
            label: 'other-consumer',
            dimmed: true,
          ),
        ],
        edges: [sourceToConsumer, otherSourceToConsumer],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'sub-source',
        ),
        highlightEdgeId: 'source-consumer',
      ),
    ],
  );

  static const journal = OnboardingCapability(
    id: 'journal',
    title: 'Journal',
    blurb: 'Interactions, bounded — not the snapshot.',
    rule:
        'The traffic journal is a bounded window of SignalDelivery envelopes. It is not the chart, the bio, or the synapse map. Past the window you get a reset snapshot, not infinite history.',
    icon: Icons.receipt_long_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer],
        edges: [sourceToConsumer],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'journal-1',
        ),
        highlightEdgeId: 'source-consumer',
      ),
    ],
  );

  static const entity = OnboardingCapability(
    id: 'entity',
    title: 'Entity',
    blurb: 'Snapshots. Not graph endpoints.',
    rule:
        'Profile, charts, and sheets are entities: current values, no journal, no synapses. Neurons fire and journal; entities persist points. The square is never a pulse target.',
    icon: Icons.inventory_2_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [source, consumer, profile],
        edges: [sourceToConsumer],
        duration: Duration(milliseconds: 500),
      ),
      OnboardingLessonFrame(
        nodes: [
          source,
          consumer,
          GraphNode(
            id: 'profile',
            label: 'profile',
            kind: GraphNodeKind.entity,
            dimmed: true,
          ),
        ],
        edges: [sourceToConsumer],
        pulse: GraphPulse(
          fromId: 'source',
          toId: 'consumer',
          signature: 'entity-1',
        ),
      ),
    ],
  );

  static const module = OnboardingCapability(
    id: 'module',
    title: 'Module',
    blurb: 'Modules contain neurons with related responsibilities.',
    rule:
        'Time groups timer neurons; other modules group their own neurons and tools. A script sends StartTimer to a named Timer neuron. The module groups the neurons; it does not receive the signal.',
    icon: Icons.extension_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [client, timeModule, timer],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [client, timeModule, timer],
        edges: [clientToTimer],
        pulse: GraphPulse(
          fromId: 'client',
          toId: 'timer',
          signature: 'module-1',
        ),
      ),
    ],
  );
}
