/// The saved C# and the active C# are separate immutable revisions.
final class BehaviorProgram {
  const BehaviorProgram({
    required this.revision,
    required this.source,
    required this.validation,
    this.inputSignalTypes = const [],
    this.outputSignalTypes = const [],
    this.diagnostics = const [],
    this.inputPolicy = 0,
  });
  final String revision, source, validation;
  final List<String> inputSignalTypes, outputSignalTypes, diagnostics;
  final int inputPolicy;
  factory BehaviorProgram.fromJson(Map<String, dynamic> json) =>
      BehaviorProgram(
        revision: json['revision'] as String,
        source: json['source'] as String,
        validation: json['validation'] as String,
        inputSignalTypes: (json['inputSignalTypes'] as List? ?? [])
            .cast<String>(),
        outputSignalTypes: (json['outputSignalTypes'] as List? ?? [])
            .cast<String>(),
        diagnostics: (json['diagnostics'] as List? ?? []).cast<String>(),
        inputPolicy: (json['inputPolicy'] as num?)?.toInt() ?? 0,
      );
}

final class SavedBehavior {
  const SavedBehavior({
    required this.id,
    required this.name,
    required this.enabled,
    this.epoch = 0,
    this.pendingCount = 0,
    this.detail,
    this.draft,
    this.active,
  });
  final String id, name;
  final bool enabled;
  final int epoch, pendingCount;
  final String? detail;
  final BehaviorProgram? draft, active;
  String get status => pendingCount > 0
      ? 'Running'
      : enabled
      ? 'Active'
      : draft?.validation == 'Invalid'
      ? 'Needs correction'
      : active != null
      ? 'Disabled'
      : 'Draft';
  factory SavedBehavior.fromJson(Map<String, dynamic> json) => SavedBehavior(
    id: json['id'] as String,
    name: json['name'] as String,
    enabled: json['enabled'] == true,
    epoch: (json['epoch'] as num?)?.toInt() ?? 0,
    pendingCount: (json['pendingCount'] as num?)?.toInt() ?? 0,
    detail: json['detail'] as String?,
    draft: json['draft'] is Map
        ? BehaviorProgram.fromJson(
            (json['draft'] as Map).cast<String, dynamic>(),
          )
        : null,
    active: json['active'] is Map
        ? BehaviorProgram.fromJson(
            (json['active'] as Map).cast<String, dynamic>(),
          )
        : null,
  );
}

abstract interface class BehaviorStudioApi {
  Future<List<SavedBehavior>> listBehaviors();
  Future<SavedBehavior> readBehavior(String name);
  Future<SavedBehavior> saveBehavior(
    String name, {
    required String source,
    required List<String> inputSignalTypes,
    required List<String> outputSignalTypes,
    String? expectedDraftRevision,
    int? inputPolicy,
  });
  Future<SavedBehavior> enableBehavior(
    String name, {
    String? expectedDraftRevision,
  });
  Future<SavedBehavior> disableBehavior(String name);
  Future<SavedBehavior> invokeBehavior(
    String name, {
    required String inputType,
    required Map<String, dynamic> input,
  });
}
