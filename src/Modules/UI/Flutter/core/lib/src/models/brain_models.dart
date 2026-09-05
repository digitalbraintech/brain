/// An owner-scoped observation of actual neurons and source-owned synapses.
final class BrainSnapshot {
  const BrainSnapshot({
    required this.rootId,
    required this.observedAt,
    this.scope = '',
    this.truncated = false,
    this.nodes = const [],
    this.synapses = const [],
    this.activity = const [],
  });
  final String rootId, scope;
  final DateTime observedAt;
  final bool truncated;
  final List<BrainNeuron> nodes;
  final List<BrainSynapse> synapses;
  final List<BrainActivity> activity;

  /// Temporary observed requests, independent of persisted subscriptions.
  List<BrainActivity> get activeDelegations {
    final latest = <String, BrainActivity>{};
    for (final event in activity) {
      if (event.kind != 'delegation' || event.operationId == null) continue;
      final key = '${event.neuronId}:${event.operationId}';
      final previous = latest[key];
      if (previous == null || event.sequence > previous.sequence) {
        latest[key] = event;
      }
    }
    return latest.values
        .where((event) => event.state == 'started' && event.targetId != null)
        .toList(growable: false);
  }

  factory BrainSnapshot.fromJson(Map<String, dynamic> json) => BrainSnapshot(
    rootId: json['rootId'] as String,
    observedAt: DateTime.parse(json['observedAt'] as String),
    scope: json['scope'] as String? ?? '',
    truncated: json['truncated'] == true,
    nodes: _objects(
      json['nodes'],
    ).map(BrainNeuron.fromJson).toList(growable: false),
    synapses: _objects(
      json['synapses'],
    ).map(BrainSynapse.fromJson).toList(growable: false),
    activity: _objects(
      json['activity'],
    ).map(BrainActivity.fromJson).toList(growable: false),
  );
}

final class BrainNeuron {
  const BrainNeuron({
    required this.id,
    required this.type,
    required this.name,
    required this.label,
    required this.module,
    this.iconKey,
    this.role = 'observed',
    this.status = 'Idle',
    this.handledSignals = const [],
    this.incomingSequence = 0,
    this.outgoingSequence = 0,
    this.lastActivityAt,
    this.outputSignals = const [],
    this.activeRevision,
    this.draftRevision,
    this.isInfrastructure = false,
    this.inputPolicy = 0,
  });
  final String id, type, name, label, module, role, status;
  final String? iconKey;
  final List<String> handledSignals;
  final int incomingSequence, outgoingSequence;
  final DateTime? lastActivityAt;
  final List<String> outputSignals;
  final String? activeRevision, draftRevision;
  final bool isInfrastructure;
  final int inputPolicy;
  factory BrainNeuron.fromJson(Map<String, dynamic> j) => BrainNeuron(
    id: j['id'] as String,
    type: j['type'] as String,
    name: j['name'] as String,
    label: j['label'] as String,
    module: j['module'] as String,
    iconKey: j['iconKey'] as String?,
    role: j['role'] as String? ?? 'observed',
    status: j['status'] as String? ?? 'Idle',
    handledSignals: (j['handledSignals'] as List? ?? []).cast<String>(),
    incomingSequence: (j['incomingSequence'] as num?)?.toInt() ?? 0,
    outgoingSequence: (j['outgoingSequence'] as num?)?.toInt() ?? 0,
    lastActivityAt: _date(j['lastActivityAt']),
    outputSignals: (j['outputSignals'] as List? ?? []).cast<String>(),
    activeRevision: j['activeRevision'] as String?,
    draftRevision: j['draftRevision'] as String?,
    isInfrastructure: j['isInfrastructure'] == true,
    inputPolicy: (j['inputPolicy'] as num?)?.toInt() ?? 0,
  );
}

final class BrainSynapse {
  const BrainSynapse({
    required this.id,
    required this.sourceId,
    required this.targetId,
    required this.signalType,
    required this.kind,
    this.weight = 0,
    this.fireCount = 0,
    this.lastFiredAt,
    this.isBlocking = false,
    this.canUnsubscribe = false,
  });
  final String id, sourceId, targetId, signalType, kind;
  final double weight;
  final int fireCount;
  final DateTime? lastFiredAt;
  final bool isBlocking, canUnsubscribe;
  factory BrainSynapse.fromJson(Map<String, dynamic> j) => BrainSynapse(
    id: j['id'] as String,
    sourceId: j['sourceId'] as String,
    targetId: j['targetId'] as String,
    signalType: j['signalType'] as String,
    kind: j['kind'] as String,
    weight: (j['weight'] as num?)?.toDouble() ?? 0,
    fireCount: (j['fireCount'] as num?)?.toInt() ?? 0,
    lastFiredAt: _date(j['lastFiredAt']),
    isBlocking: j['isBlocking'] == true,
    canUnsubscribe: j['canUnsubscribe'] == true,
  );
}

final class BrainActivity {
  const BrainActivity({
    required this.id,
    required this.neuronId,
    required this.direction,
    required this.sequence,
    required this.signalType,
    required this.timestamp,
    this.callerId,
    this.correlationId,
    this.summary = '',
    this.payloadPreview,
    this.operationId,
    this.kind,
    this.state,
    this.name,
    this.targetId,
    this.server,
    this.durationMs,
    this.resultPreview,
    this.failureCode,
    this.isError = false,
    this.truncated = false,
  });
  final String id, neuronId, direction, signalType, summary;
  final String? callerId, correlationId;
  final Object? payloadPreview;
  final String? operationId, kind, state, name, targetId, server, resultPreview;
  final String? failureCode;
  final double? durationMs;
  final bool isError, truncated;
  final int sequence;
  final DateTime? timestamp;
  factory BrainActivity.fromJson(Map<String, dynamic> j) => BrainActivity(
    id: j['id'] as String,
    neuronId: j['neuronId'] as String,
    direction: j['direction'] as String,
    sequence: (j['sequence'] as num).toInt(),
    signalType: j['signalType'] as String,
    timestamp: _date(j['timestamp']),
    callerId: j['callerId'] as String?,
    correlationId: j['correlationId'] as String?,
    summary: j['summary'] as String? ?? '',
    payloadPreview: j['payloadPreview'],
    operationId: j['operationId'] as String?,
    kind: j['kind'] as String?,
    state: j['state'] as String?,
    name: j['name'] as String?,
    targetId: j['targetId'] as String?,
    server: j['server'] as String?,
    durationMs: (j['durationMs'] as num?)?.toDouble(),
    resultPreview: j['resultPreview'] as String?,
    failureCode: j['failureCode'] as String?,
    isError: j['isError'] == true,
    truncated: j['truncated'] == true,
  );
}

Iterable<Map<String, dynamic>> _objects(Object? value) =>
    (value as List? ?? []).map((item) => (item as Map).cast<String, dynamic>());
DateTime? _date(Object? value) =>
    value is String ? DateTime.tryParse(value) : null;

typedef ReadBrain = Future<BrainSnapshot> Function();
typedef WatchBrain = Stream<BrainSnapshot> Function();
typedef SetBrainSubscription =
    Future<void> Function({
      required String sourceId,
      required String targetId,
      required String signalType,
      required bool subscribed,
    });
