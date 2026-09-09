import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

/// Focus is a view of observed deliveries, not a mutation of subscriptions.
BrainSnapshot activityCanvas(
  BrainSnapshot graph,
  Iterable<ExecutionActivity> activities,
) {
  final selected = activities.toList();
  final correlations = selected.map((a) => a.correlationId).toSet();
  final ids = selected.expand((a) => a.participantNeuronIds).toSet();
  final events = selected.expand((a) => a.events).toList();
  for (final event in events) {
    ids.add(event.sourceNeuronId);
    if (event.targetNeuronId != null) ids.add(event.targetNeuronId!);
  }
  final known = {for (final node in graph.nodes) node.id: node};
  final nodes = [
    for (final id in ids)
      if (id.isNotEmpty)
        _activityNode(
          known[id] ??
              BrainNeuron(
                id: id,
                type: id.split(':').first,
                name: id.contains(':') ? id.substring(id.indexOf(':') + 1) : id,
                label: id.contains(':')
                    ? id.substring(id.indexOf(':') + 1)
                    : id,
                module: 'Observed execution',
                role: 'observed',
                status: 'Observed',
              ),
          events,
        ),
  ];
  final links = <String, BrainSynapse>{};
  for (final event in events) {
    final target = event.targetNeuronId;
    if (target == null || target.isEmpty || event.sourceNeuronId.isEmpty) {
      continue;
    }
    final bound = graph.synapses
        .where(
          (edge) =>
              edge.sourceId == event.sourceNeuronId &&
              edge.targetId == target &&
              (edge.signalType == event.signalType || edge.signalType == '*'),
        )
        .firstOrNull;
    if (bound != null) {
      links[bound.id] = bound;
      continue;
    }
    final id = 'observed:${event.sourceNeuronId}:$target:${event.signalType}';
    links[id] = BrainSynapse(
      id: id,
      sourceId: event.sourceNeuronId,
      targetId: target,
      signalType: event.signalType,
      kind: 'Observed',
      fireCount: (links[id]?.fireCount ?? 0) + 1,
      lastFiredAt: event.timestamp,
    );
  }
  return BrainSnapshot(
    rootId: graph.rootId,
    observedAt: graph.observedAt,
    scope: 'Selected activity',
    truncated: graph.truncated,
    nodes: nodes,
    synapses: links.values.toList(),
    activity: graph.activity
        .where((event) => correlations.contains(event.correlationId))
        .toList(),
    correlations: graph.correlations
        .where((c) => correlations.contains(c.correlationId))
        .toList(),
  );
}

BrainNeuron _activityNode(
  BrainNeuron node,
  List<ExecutionActivityEvent> events,
) {
  final operations = <String, ExecutionActivityEvent>{};
  for (final event in events.where(
    (event) =>
        event.targetNeuronId == node.id ||
        (event.targetNeuronId == null && event.sourceNeuronId == node.id),
  )) {
    final previous = operations[event.operationId];
    if (previous == null || !event.timestamp.isBefore(previous.timestamp)) {
      operations[event.operationId] = event;
    }
  }
  final phases = operations.values.map((event) => event.phase).toSet();
  final status = phases.contains('running')
      ? 'Running'
      : phases.contains('waiting')
      ? 'Waiting'
      : phases.contains('failed')
      ? 'Failed'
      : phases.contains('cancelled')
      ? 'Cancelled'
      : phases.contains('completed')
      ? 'Completed'
      : 'Observed';
  return BrainNeuron(
    id: node.id,
    type: node.type,
    name: node.name,
    label: node.label,
    module: node.module,
    iconKey: node.iconKey,
    role: node.role,
    status: status,
    handledSignals: node.handledSignals,
    incomingSequence: node.incomingSequence,
    outgoingSequence: node.outgoingSequence,
    lastActivityAt: node.lastActivityAt,
    isInfrastructure: node.isInfrastructure,
  );
}
