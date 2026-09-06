/// Source-owned execution facts. Lifecycle is supplied by the runtime.
final class ExecutionActivity {
  const ExecutionActivity({
    required this.id,
    required this.correlationId,
    required this.rootSignalId,
    required this.triggerName,
    required this.title,
    required this.status,
    required this.startedAt,
    required this.updatedAt,
    this.commandId,
    this.detail,
    this.participantNeuronIds = const [],
    this.events = const [],
    this.version = 0,
  });
  final String id, correlationId, rootSignalId, triggerName, title, status;
  final String? commandId, detail;
  final DateTime startedAt, updatedAt;
  final List<String> participantNeuronIds;
  final List<ExecutionActivityEvent> events;
  final int version;
  factory ExecutionActivity.fromJson(Map<String, dynamic> j) =>
      ExecutionActivity(
        id: j['id'] as String,
        correlationId: j['correlationId'] as String,
        rootSignalId: j['rootSignalId'] as String? ?? '',
        triggerName: j['triggerName'] as String? ?? '',
        title: j['title'] as String? ?? '',
        status: j['status'] as String? ?? 'observed',
        version: (j['version'] as num?)?.toInt() ?? 0,
        commandId: j['commandId'] as String?,
        detail: j['detail'] as String?,
        startedAt: DateTime.parse(j['startedAt'] as String),
        updatedAt: DateTime.parse(j['updatedAt'] as String),
        participantNeuronIds: (j['participantNeuronIds'] as List? ?? const [])
            .cast<String>(),
        events: (j['events'] as List? ?? const [])
            .whereType<Map>()
            .map(
              (e) =>
                  ExecutionActivityEvent.fromJson(Map<String, dynamic>.from(e)),
            )
            .toList(growable: false),
      );
}

final class ExecutionActivityEvent {
  const ExecutionActivityEvent({
    required this.operationId,
    required this.signalId,
    required this.sourceNeuronId,
    required this.signalType,
    required this.phase,
    required this.timestamp,
    this.causationId,
    this.targetNeuronId,
    this.behaviorRevision,
  });
  final String operationId, signalId, sourceNeuronId, signalType, phase;
  final String? causationId, targetNeuronId, behaviorRevision;
  final DateTime timestamp;
  factory ExecutionActivityEvent.fromJson(Map<String, dynamic> j) =>
      ExecutionActivityEvent(
        operationId: j['operationId'] as String? ?? '',
        signalId: j['signalId'] as String? ?? '',
        sourceNeuronId: j['sourceNeuronId'] as String? ?? '',
        targetNeuronId: j['targetNeuronId'] as String?,
        signalType: j['signalType'] as String? ?? '',
        phase: j['phase'] as String? ?? '',
        causationId: j['causationId'] as String?,
        behaviorRevision: j['behaviorRevision'] as String?,
        timestamp: DateTime.parse(j['timestamp'] as String),
      );
}

typedef WatchExecutionActivities = Stream<List<ExecutionActivity>> Function();
