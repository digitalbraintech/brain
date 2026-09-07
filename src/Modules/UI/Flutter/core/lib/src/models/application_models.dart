final class ApplicationSource {
  const ApplicationSource({
    required this.key,
    required this.sourceRevision,
    required this.source,
    this.path = 'application.cs',
    this.activeRevision,
    this.pendingActivationRevision,
  });
  final String key, sourceRevision, source;
  final String path;
  final String? activeRevision, pendingActivationRevision;
  factory ApplicationSource.fromJson(Map<String, dynamic> json) =>
      ApplicationSource(
        key: json['key'] as String,
        sourceRevision: json['sourceRevision'] as String,
        source: json['source'] as String,
        path: json['path'] as String? ?? 'application.cs',
        activeRevision: json['activeRevision'] as String?,
        pendingActivationRevision: json['pendingActivationRevision'] as String?,
      );
}

final class ApplicationSummary {
  const ApplicationSummary({
    required this.key,
    required this.sourceRevision,
    required this.validated,
    this.activeRevision,
    this.pendingActivationRevision,
  });
  final String key, sourceRevision;
  final bool validated;
  final String? activeRevision, pendingActivationRevision;
  factory ApplicationSummary.fromJson(Map<String, dynamic> json) =>
      ApplicationSummary(
        key: json['key'] as String,
        sourceRevision: json['sourceRevision'] as String,
        validated: json['validated'] == true,
        activeRevision: json['activeRevision'] as String?,
        pendingActivationRevision: json['pendingActivationRevision'] as String?,
      );
}

final class ApplicationValidation {
  const ApplicationValidation({
    required this.sourceRevision,
    required this.succeeded,
    this.diagnostics,
  });
  final String sourceRevision;
  final bool succeeded;
  final String? diagnostics;
  factory ApplicationValidation.fromJson(Map<String, dynamic> json) =>
      ApplicationValidation(
        sourceRevision: json['sourceRevision'] as String,
        succeeded: json['succeeded'] == true,
        diagnostics: json['diagnostics'] as String?,
      );
}

final class ApplicationActivation {
  const ApplicationActivation({
    required this.key,
    required this.sourceRevision,
    required this.artifactRevision,
  });
  final String key, sourceRevision, artifactRevision;
  factory ApplicationActivation.fromJson(Map<String, dynamic> json) =>
      ApplicationActivation(
        key: json['key'] as String,
        sourceRevision: json['sourceRevision'] as String,
        artifactRevision: json['artifactRevision'] as String,
      );
}

final class ApplicationScenarioExample {
  const ApplicationScenarioExample({
    required this.name,
    required this.passed,
    this.actualJson,
    this.error,
  });
  final String name;
  final bool passed;
  final String? actualJson, error;
  factory ApplicationScenarioExample.fromJson(Map<String, dynamic> json) =>
      ApplicationScenarioExample(
        name: json['name'] as String,
        passed: json['passed'] == true,
        actualJson: json['actualJson'] as String?,
        error: json['error'] as String?,
      );
}

final class ApplicationScenarioReport {
  const ApplicationScenarioReport({
    required this.sourceRevision,
    required this.artifactRevision,
    required this.passed,
    required this.examples,
  });
  final String sourceRevision, artifactRevision;
  final bool passed;
  final List<ApplicationScenarioExample> examples;
  factory ApplicationScenarioReport.fromJson(Map<String, dynamic> json) =>
      ApplicationScenarioReport(
        sourceRevision: json['sourceRevision'] as String,
        artifactRevision: json['artifactRevision'] as String,
        passed: json['passed'] == true,
        examples: (json['examples'] as List)
            .map(
              (value) => ApplicationScenarioExample.fromJson(
                (value as Map).cast<String, dynamic>(),
              ),
            )
            .toList(),
      );
}

abstract interface class ApplicationStudioApi {
  Future<String> applicationTemplate(String key);
  Future<List<ApplicationSummary>> listApplications();
  Future<ApplicationSource> readApplication(String key);
  Future<List<String>> listApplicationFiles(String key);
  Future<ApplicationSource> readApplicationFile(String key, String path);
  Future<ApplicationSource> saveApplication(
    String key, {
    required String source,
    required String? expectedRevision,
  });
  Future<ApplicationSource> saveApplicationFile(
    String key, {
    required String path,
    required String source,
    required String expectedRevision,
  });
  Future<ApplicationScenarioReport?> readApplicationScenarios(
    String key, {
    required String expectedSourceRevision,
  });
  Future<ApplicationScenarioReport> runApplicationScenarios(
    String key, {
    required String expectedSourceRevision,
  });
  Future<ApplicationValidation> validateApplication(
    String key, {
    required String expectedSourceRevision,
  });
  Future<ApplicationActivation> activateApplication(
    String key, {
    required String expectedSourceRevision,
  });
}
