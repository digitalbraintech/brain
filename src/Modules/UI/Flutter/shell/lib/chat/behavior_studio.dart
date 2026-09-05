import 'dart:async';
import 'dart:convert';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

/// An editor of stored neuron programs. It owns no executable graph state.
class BehaviorLibrary extends StatefulWidget {
  const BehaviorLibrary({
    super.key,
    required this.api,
    required this.changes,
    this.initialName,
    this.readGraph,
  });
  final BehaviorStudioApi api;
  final Listenable changes;
  final String? initialName;
  final BrainSnapshot? Function()? readGraph;
  @override
  State<BehaviorLibrary> createState() => _BehaviorLibraryState();
}

class _BehaviorLibraryState extends State<BehaviorLibrary> {
  List<SavedBehavior> _items = [];
  String? _selected, _error;
  bool _loading = false;
  @override
  void initState() {
    super.initState();
    _selected = widget.initialName;
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
      final items = await widget.api.listBehaviors();
      if (mounted) {
        setState(() {
          _items = items;
          _error = null;
        });
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => _error = 'Cannot read saved behaviors. Reconnect and refresh.',
        );
      }
    } finally {
      _loading = false;
    }
  }

  Future<void> _create() async {
    final controller = TextEditingController();
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('New behavior'),
        content: TextField(
          key: const Key('new_behavior_name'),
          controller: controller,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: 'Name',
            hintText: 'my-review',
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              final name = controller.text.trim();
              if (name.isNotEmpty) Navigator.pop(context, name);
            },
            child: const Text('Create'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (name != null && mounted) setState(() => _selected = name);
  }

  @override
  Widget build(BuildContext context) => Dialog(
    insetPadding: const EdgeInsets.all(20),
    child: SizedBox(
      width: 1100,
      height: 760,
      child: Column(
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 12, 12, 8),
            child: Row(
              children: [
                const Expanded(
                  child: Text(
                    'Behavior Studio',
                    style: TextStyle(fontSize: 22, fontWeight: FontWeight.w600),
                  ),
                ),
                TextButton.icon(
                  onPressed: _create,
                  icon: const Icon(Icons.add),
                  label: const Text('New behavior'),
                ),
                IconButton(
                  onPressed: () => Navigator.pop(context),
                  tooltip: 'Close studio',
                  icon: const Icon(Icons.close),
                ),
              ],
            ),
          ),
          Expanded(
            child: LayoutBuilder(
              builder: (context, size) {
                final library = ListView(
                  children: [
                    const Padding(
                      padding: EdgeInsets.all(16),
                      child: Text(
                        'Saved C# programs. Subscriptions live on their source neurons.',
                      ),
                    ),
                    if (_error != null)
                      Padding(
                        padding: const EdgeInsets.all(12),
                        child: Text(_error!),
                      ),
                    if (_items.isEmpty && _error == null)
                      const Padding(
                        padding: EdgeInsets.all(16),
                        child: Text(
                          'No saved behaviors yet. Create one here or ask Ino.',
                        ),
                      ),
                    for (final behavior in _items)
                      ListTile(
                        key: ValueKey('behavior_${behavior.name}'),
                        leading: const Icon(Icons.code),
                        title: Text(behavior.name),
                        subtitle: Text(behavior.status),
                        selected: _selected == behavior.name,
                        onTap: () => setState(() => _selected = behavior.name),
                      ),
                  ],
                );
                final editor = _selected == null
                    ? const Center(
                        child: Text(
                          'Select a behavior to inspect its C# and revisions.',
                        ),
                      )
                    : BehaviorEditor(
                        key: ValueKey(_selected),
                        api: widget.api,
                        name: _selected!,
                        changes: widget.changes,
                        readGraph: widget.readGraph,
                      );
                if (size.maxWidth < 650) {
                  return Column(
                    children: [
                      SizedBox(
                        height: _selected == null ? 230 : 110,
                        child: library,
                      ),
                      const Divider(height: 1),
                      Expanded(child: editor),
                    ],
                  );
                }
                return Row(
                  children: [
                    SizedBox(width: 250, child: library),
                    const VerticalDivider(width: 1),
                    Expanded(child: editor),
                  ],
                );
              },
            ),
          ),
        ],
      ),
    ),
  );
}

class BehaviorEditor extends StatefulWidget {
  const BehaviorEditor({
    super.key,
    required this.api,
    required this.name,
    required this.changes,
    this.readGraph,
  });
  final BehaviorStudioApi api;
  final String name;
  final Listenable changes;
  final BrainSnapshot? Function()? readGraph;
  @override
  State<BehaviorEditor> createState() => _BehaviorEditorState();
}

class _BehaviorEditorState extends State<BehaviorEditor> {
  final _source = TextEditingController(
    text:
        'await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);\n'
        'return digitalBrain.Input<Note>();',
  );
  final _inputs = TextEditingController(text: 'Note');
  final _outputs = TextEditingController(text: 'Note');
  SavedBehavior? _behavior;
  String? _editingRevision, _message;
  int _inputPolicy = 0;
  bool _busy = false, _reading = false, _dirty = false, _setting = false;
  @override
  void initState() {
    super.initState();
    for (final value in [_source, _inputs, _outputs]) {
      value.addListener(_edited);
    }
    widget.changes.addListener(_observe);
    unawaited(_reload());
  }

  @override
  void dispose() {
    widget.changes.removeListener(_observe);
    for (final value in [_source, _inputs, _outputs]) {
      value.dispose();
    }
    super.dispose();
  }

  void _edited() {
    if (!_setting && mounted) setState(() => _dirty = true);
  }

  void _observe() {
    unawaited(_reload());
  }

  void _accept(SavedBehavior next, {bool replaceSource = false}) {
    _behavior = next;
    if (!_dirty || replaceSource) {
      final program = next.draft ?? next.active;
      if (program != null) {
        _setting = true;
        _source.text = program.source;
        _inputs.text = program.inputSignalTypes.join(', ');
        _outputs.text = program.outputSignalTypes.join(', ');
        _inputPolicy = program.inputPolicy;
        _setting = false;
        _editingRevision = program.revision;
      }
      _dirty = false;
    }
  }

  Future<void> _reload() async {
    if (_reading) return;
    _reading = true;
    try {
      final next = await widget.api.readBehavior(widget.name);
      if (mounted) setState(() => _accept(next));
    } catch (_) {
      if (mounted) {
        setState(
          () => _message =
              'Cannot refresh this behavior. Your edits are preserved.',
        );
      }
    } finally {
      _reading = false;
    }
  }

  List<String> _types(String value) => value
      .split(',')
      .map((item) => item.trim())
      .where((item) => item.isNotEmpty)
      .toSet()
      .toList();
  Future<void> _command(
    Future<SavedBehavior> Function() action, {
    bool saved = false,
  }) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _message = null;
    });
    try {
      final next = await action();
      if (mounted) {
        setState(() {
          _accept(next, replaceSource: saved);
        });
      }
    } catch (_) {
      if (mounted) {
        setState(
          () => _message =
              'The operation was not accepted. Refresh state and check diagnostics; your edits are preserved.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _invoke() async {
    final types = _behavior?.active?.inputSignalTypes ?? [];
    if (types.isEmpty) return;
    var type = types.first;
    final input = TextEditingController(
      text: type == 'Note' ? '{"text":"Hello"}' : '{}',
    );
    String? error;
    final payload = await showDialog<(String, Map<String, dynamic>)>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: const Text('Run active revision'),
          content: SizedBox(
            width: 460,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: type,
                  decoration: const InputDecoration(labelText: 'Input signal'),
                  items: [
                    for (final value in types)
                      DropdownMenuItem(value: value, child: Text(value)),
                  ],
                  onChanged: (value) => update(() => type = value!),
                ),
                const SizedBox(height: 12),
                TextField(
                  controller: input,
                  minLines: 4,
                  maxLines: 10,
                  decoration: const InputDecoration(labelText: 'Signal JSON'),
                ),
                if (error != null) Text(error!),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                try {
                  final value = jsonDecode(input.text);
                  if (value is! Map) throw const FormatException();
                  Navigator.pop(context, (type, value.cast<String, dynamic>()));
                } catch (_) {
                  update(
                    () => error = 'Enter a JSON object matching this signal.',
                  );
                }
              },
              child: const Text('Run'),
            ),
          ],
        ),
      ),
    );
    input.dispose();
    if (payload != null && mounted) {
      await _command(
        () => widget.api.invokeBehavior(
          widget.name,
          inputType: payload.$1,
          input: payload.$2,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final behavior = _behavior;
    final draft = behavior?.draft;
    final active = behavior?.active;
    final graph = widget.readGraph?.call();
    final connections =
        graph?.synapses
            .where(
              (edge) =>
                  edge.sourceId == behavior?.id ||
                  edge.targetId == behavior?.id,
            )
            .toList() ??
        <BrainSynapse>[];
    return Padding(
      padding: const EdgeInsets.all(18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  widget.name,
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(
                _dirty ? 'Unsaved changes' : behavior?.status ?? 'New draft',
              ),
              IconButton(
                onPressed: _busy ? null : _reload,
                tooltip: 'Refresh status',
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
          Text(
            'Draft: ${draft?.validation ?? 'Not saved'} · Active: ${active == null ? 'None' : active.revision.substring(0, 8)} · Pending: ${behavior?.pendingCount ?? 0}',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          if (graph != null)
            ExpansionTile(
              dense: true,
              tilePadding: EdgeInsets.zero,
              title: Text('Connections (${connections.length})'),
              children: [
                if (connections.isEmpty)
                  const Align(
                    alignment: Alignment.centerLeft,
                    child: Text(
                      'No subscriptions yet. Ask Ino to connect a source, or add a subscription on the graph.',
                    ),
                  ),
                for (final edge in connections)
                  Align(
                    alignment: Alignment.centerLeft,
                    child: Text(
                      '${edge.sourceId == behavior?.id ? 'Output to' : 'Input from'} ${graph.nodes.where((node) => node.id == (edge.sourceId == behavior?.id ? edge.targetId : edge.sourceId)).map((node) => '${node.label} (${node.status})').firstOrNull ?? 'source'} · ${edge.signalType} · ${edge.kind}',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
              ],
            ),
          const SizedBox(height: 10),
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _inputs,
                  decoration: const InputDecoration(
                    labelText: 'Input signals (comma separated)',
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: TextField(
                  controller: _outputs,
                  decoration: const InputDecoration(
                    labelText: 'Output signals (comma separated)',
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            children: [
              for (final option in const [
                (1, 'Latest per subject'),
                (2, 'Observe from activation'),
                (4, 'Once per version'),
              ])
                FilterChip(
                  label: Text(option.$2),
                  selected: (_inputPolicy & option.$1) != 0,
                  onSelected: _busy
                      ? null
                      : (selected) => setState(() {
                          _inputPolicy = selected
                              ? _inputPolicy | option.$1
                              : _inputPolicy & ~option.$1;
                          _dirty = true;
                        }),
                ),
            ],
          ),
          Text(
            _inputPolicy == 0
                ? 'Process every input event.'
                : 'Input policy applies when this draft is activated. Version policies require versioned signals.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const Text(
            'C# handler · ConnectAsync(args), Input<T>(), CancellationToken. Return a signal to publish to subscribers.',
            style: TextStyle(fontSize: 11),
          ),
          const SizedBox(height: 6),
          Expanded(
            child: TextField(
              key: const Key('behavior_source'),
              controller: _source,
              expands: true,
              minLines: null,
              maxLines: null,
              keyboardType: TextInputType.multiline,
              textAlignVertical: TextAlignVertical.top,
              style: const TextStyle(fontFamily: 'Consolas', fontSize: 13),
              decoration: const InputDecoration(border: OutlineInputBorder()),
            ),
          ),
          if ((draft?.diagnostics ?? []).isNotEmpty)
            ConstrainedBox(
              constraints: const BoxConstraints(maxHeight: 110),
              child: SingleChildScrollView(
                child: SelectableText(
                  draft!.diagnostics.join('\n'),
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.error,
                    fontSize: 12,
                  ),
                ),
              ),
            ),
          if (_message != null || behavior?.detail != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Text(
                _message ?? behavior!.detail!,
                style: const TextStyle(fontSize: 12),
              ),
            ),
          if (active != null && draft?.revision != active.revision)
            ExpansionTile(
              dense: true,
              title: const Text('Currently active C#'),
              children: [
                ConstrainedBox(
                  constraints: const BoxConstraints(maxHeight: 120),
                  child: SingleChildScrollView(
                    child: SelectableText(active.source),
                  ),
                ),
              ],
            ),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              FilledButton(
                onPressed: _busy
                    ? null
                    : () => _command(
                        () => widget.api.saveBehavior(
                          widget.name,
                          source: _source.text,
                          inputSignalTypes: _types(_inputs.text),
                          outputSignalTypes: _types(_outputs.text),
                          expectedDraftRevision: _editingRevision,
                          inputPolicy: _inputPolicy,
                        ),
                        saved: true,
                      ),
                child: const Text('Save draft'),
              ),
              OutlinedButton(
                onPressed: _busy || _dirty || draft == null
                    ? null
                    : () => _command(
                        () => widget.api.enableBehavior(
                          widget.name,
                          expectedDraftRevision: draft.revision,
                        ),
                      ),
                child: const Text('Activate'),
              ),
              OutlinedButton(
                onPressed: _busy || !(_behavior?.enabled ?? false)
                    ? null
                    : _invoke,
                child: const Text('Run'),
              ),
              TextButton(
                onPressed: _busy || !(_behavior?.enabled ?? false)
                    ? null
                    : () => _command(
                        () => widget.api.disableBehavior(widget.name),
                      ),
                child: const Text('Disable'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
