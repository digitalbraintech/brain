import 'dart:async';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

class ApplicationLibrary extends StatefulWidget {
  const ApplicationLibrary({
    super.key,
    required this.api,
    required this.changes,
    this.initialKey,
  });
  final ApplicationStudioApi api;
  final Listenable changes;
  final String? initialKey;
  @override
  State<ApplicationLibrary> createState() => _ApplicationLibraryState();
}

class _ApplicationLibraryState extends State<ApplicationLibrary> {
  List<ApplicationSummary> _items = [];
  String? _selected;
  bool _loading = false;
  bool _creating = false;
  bool _editorDirty = false;

  @override
  void initState() {
    super.initState();
    _selected = widget.initialKey;
    widget.changes.addListener(_reload);
    unawaited(_reload());
  }

  @override
  void dispose() {
    widget.changes.removeListener(_reload);
    super.dispose();
  }

  Future<void> _reload() async {
    if (_loading) return;
    _loading = true;
    try {
      final items = await widget.api.listApplications();
      if (mounted) setState(() => _items = items);
    } finally {
      _loading = false;
    }
  }

  Future<void> _create() async {
    var name = '';
    final key = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('New application'),
        content: TextField(
          key: const Key('new_application_key'),
          onChanged: (value) => name = value,
          autofocus: true,
          decoration: const InputDecoration(labelText: 'Application key'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              final value = name.trim();
              if (value.isNotEmpty) Navigator.pop(context, value);
            },
            child: const Text('Create'),
          ),
        ],
      ),
    );
    if (key != null && mounted) {
      setState(() {
        _selected = key;
        _creating = true;
      });
    }
  }

  @override
  Widget build(BuildContext context) => Dialog(
    child: SizedBox(
      width: 1050,
      height: 720,
      child: Column(
        children: [
          ListTile(
            title: const Text('Application Studio'),
            trailing: TextButton.icon(
              onPressed: _create,
              icon: const Icon(Icons.add),
              label: const Text('New application'),
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: Row(
              children: [
                SizedBox(
                  width: 240,
                  child: ListView(
                    children: [
                      for (final application in _items)
                        ListTile(
                          key: ValueKey('application_${application.key}'),
                          title: Text(application.key),
                          subtitle: Text(
                            application.activeRevision != null
                                ? 'Active'
                                : application.validated
                                ? 'Validated'
                                : 'Draft',
                          ),
                          selected: _selected == application.key,
                          onTap: () {
                            if (_editorDirty) return;
                            setState(() {
                              _selected = application.key;
                              _creating = false;
                              _editorDirty = false;
                            });
                          },
                        ),
                    ],
                  ),
                ),
                const VerticalDivider(width: 1),
                Expanded(
                  child: _selected == null
                      ? const Center(
                          child: Text('Select or create an application.'),
                        )
                      : ApplicationEditor(
                          key: ValueKey(_selected),
                          api: widget.api,
                          applicationKey: _selected!,
                          isNew: _creating,
                          onDirtyChanged: (dirty) =>
                              setState(() => _editorDirty = dirty),
                          onApplicationChanged: _reload,
                        ),
                ),
              ],
            ),
          ),
        ],
      ),
    ),
  );
}

class ApplicationEditor extends StatefulWidget {
  const ApplicationEditor({
    super.key,
    required this.api,
    required this.applicationKey,
    this.isNew = false,
    this.onDirtyChanged,
    this.onApplicationChanged,
  });
  final ApplicationStudioApi api;
  final String applicationKey;
  final bool isNew;
  final ValueChanged<bool>? onDirtyChanged;
  final VoidCallback? onApplicationChanged;
  @override
  State<ApplicationEditor> createState() => _ApplicationEditorState();
}

class _ApplicationEditorState extends State<ApplicationEditor> {
  final _source = TextEditingController();
  ApplicationSource? _saved;
  ApplicationValidation? _validation;
  ApplicationScenarioReport? _scenarioReport;
  String? _message;
  List<String> _files = const [];
  String? _selectedPath, _rootPath;
  bool _busy = false, _dirty = false, _setting = false;

  @override
  void initState() {
    super.initState();
    _source.addListener(() {
      if (!_setting && mounted) {
        setState(() {
          _dirty = true;
          _validation = null;
          _scenarioReport = null;
        });
        widget.onDirtyChanged?.call(true);
      }
    });
    unawaited(_read());
  }

  @override
  void dispose() {
    _source.dispose();
    super.dispose();
  }

  Future<void> _read() async {
    try {
      if (widget.isNew) {
        final starter = await widget.api.applicationTemplate(
          widget.applicationKey,
        );
        if (!mounted) return;
        setState(() {
          _source.text = starter;
          _dirty = true;
        });
        return;
      }
      final saved = await widget.api.readApplication(widget.applicationKey);
      final files = await widget.api.listApplicationFiles(
        widget.applicationKey,
      );
      final scenarios = files.contains('acceptance.json')
          ? await widget.api.readApplicationScenarios(
              widget.applicationKey,
              expectedSourceRevision: saved.sourceRevision,
            )
          : null;
      if (!mounted) return;
      setState(() {
        _saved = saved;
        _files = files;
        _rootPath = saved.path;
        _selectedPath = saved.path;
        _scenarioReport = scenarios;
        _setting = true;
        _source.text = saved.source;
        _setting = false;
        _dirty = false;
      });
    } catch (_) {
      if (mounted) {
        setState(
          () => _message =
              'Unable to load application source. Refresh and retry.',
        );
      }
    }
  }

  Future<void> _selectFile(String? path) async {
    if (path == null || path == _selectedPath || _busy || _dirty) return;
    await _run(() async {
      final saved = path == _rootPath
          ? await widget.api.readApplication(widget.applicationKey)
          : await widget.api.readApplicationFile(widget.applicationKey, path);
      if (mounted) {
        setState(() {
          _saved = saved;
          _selectedPath = path;
          _setting = true;
          _source.text = saved.source;
          _setting = false;
          _dirty = false;
        });
        widget.onDirtyChanged?.call(false);
      }
    });
  }

  Future<void> _newFile() async {
    if (_busy || _dirty || _saved == null) return;
    final controller = TextEditingController();
    final path = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('New application file'),
        content: TextField(
          key: const Key('new_application_file_path'),
          controller: controller,
          autofocus: true,
          decoration: const InputDecoration(hintText: 'helpers.cs'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, controller.text.trim()),
            child: const Text('Create'),
          ),
        ],
      ),
    );
    if (!mounted || path == null || path.isEmpty || _files.contains(path)) {
      return;
    }
    setState(() {
      _files = [..._files, path]..sort();
      _selectedPath = path;
      _validation = null;
      _scenarioReport = null;
      _setting = true;
      _source.clear();
      _setting = false;
      _dirty = true;
    });
    widget.onDirtyChanged?.call(true);
  }

  Future<void> _run(Future<void> Function() operation) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      await operation();
    } catch (_) {
      if (mounted) {
        setState(
          () => _message =
              'The application changed or the operation failed. Refresh and retry.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _save() => _run(() async {
    final saved = _selectedPath != null && _selectedPath != _rootPath
        ? await widget.api.saveApplicationFile(
            widget.applicationKey,
            path: _selectedPath!,
            source: _source.text,
            expectedRevision: _saved!.sourceRevision,
          )
        : await widget.api.saveApplication(
            widget.applicationKey,
            source: _source.text,
            expectedRevision: _saved?.sourceRevision,
          );
    if (mounted) {
      setState(() {
        _saved = saved;
        _validation = null;
        _scenarioReport = null;
        _dirty = false;
      });
      widget.onDirtyChanged?.call(false);
      if (_rootPath == null) {
        setState(() {
          _rootPath = saved.path;
          _selectedPath = saved.path;
          _files = [saved.path];
        });
      }
      widget.onApplicationChanged?.call();
    }
  });

  Future<void> _validate() => _run(() async {
    final validation = await widget.api.validateApplication(
      widget.applicationKey,
      expectedSourceRevision: _saved!.sourceRevision,
    );
    if (mounted) setState(() => _validation = validation);
  });

  Future<void> _runExamples() => _run(() async {
    final report = await widget.api.runApplicationScenarios(
      widget.applicationKey,
      expectedSourceRevision: _saved!.sourceRevision,
    );
    if (mounted) setState(() => _scenarioReport = report);
  });

  Future<void> _activate() => _run(() async {
    final activation = await widget.api.activateApplication(
      widget.applicationKey,
      expectedSourceRevision: _saved!.sourceRevision,
    );
    if (mounted) {
      setState(
        () => _message =
            'Activated ${activation.artifactRevision.substring(0, 8)}',
      );
      widget.onApplicationChanged?.call();
    }
  });

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(18),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          widget.applicationKey,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        Text('Source revision: ${_saved?.sourceRevision ?? 'Not saved'}'),
        if (_files.isNotEmpty)
          Row(
            children: [
              Expanded(
                child: DropdownButtonFormField<String>(
                  key: const Key('application_file_selector'),
                  initialValue: _selectedPath,
                  items: _files
                      .map(
                        (path) =>
                            DropdownMenuItem(value: path, child: Text(path)),
                      )
                      .toList(),
                  onChanged: _busy || _dirty ? null : _selectFile,
                  decoration: const InputDecoration(labelText: 'File'),
                ),
              ),
              const SizedBox(width: 8),
              OutlinedButton(
                onPressed: _busy || _dirty || _saved == null ? null : _newFile,
                child: const Text('New file'),
              ),
            ],
          ),
        const SizedBox(height: 10),
        Expanded(
          child: TextField(
            key: const Key('application_source'),
            controller: _source,
            expands: true,
            minLines: null,
            maxLines: null,
            textAlignVertical: TextAlignVertical.top,
            style: const TextStyle(fontFamily: 'Consolas', fontSize: 13),
            decoration: const InputDecoration(
              labelText: 'Complete C# application file',
              border: OutlineInputBorder(),
            ),
          ),
        ),
        if (_validation?.diagnostics case final diagnostics?)
          SelectableText(diagnostics),
        if (_scenarioReport case final report?)
          for (final example in report.examples)
            Text(
              '${example.name}: ${example.passed ? 'passed' : 'failed'}'
              '${example.actualJson == null ? '' : ' — actual ${example.actualJson}'}'
              '${example.error == null ? '' : ' — ${example.error}'}',
            ),
        if (_message case final message?) Text(message),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: [
            FilledButton(
              onPressed: _busy ? null : _save,
              child: const Text('Save'),
            ),
            OutlinedButton(
              onPressed: _busy || _dirty || _saved == null ? null : _validate,
              child: const Text('Validate'),
            ),
            if (_files.contains('acceptance.json'))
              OutlinedButton(
                onPressed: _busy || _dirty || _saved == null
                    ? null
                    : _runExamples,
                child: const Text('Run examples'),
              ),
            OutlinedButton(
              onPressed:
                  _busy ||
                      _dirty ||
                      _validation?.succeeded != true ||
                      _validation?.sourceRevision != _saved?.sourceRevision ||
                      (_files.contains('acceptance.json') &&
                          (_scenarioReport?.passed != true ||
                              _scenarioReport?.sourceRevision !=
                                  _saved?.sourceRevision))
                  ? null
                  : _activate,
              child: const Text('Activate'),
            ),
          ],
        ),
      ],
    ),
  );
}
