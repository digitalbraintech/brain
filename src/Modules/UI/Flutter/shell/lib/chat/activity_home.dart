part of 'graph_home_screen.dart';

extension _ActivityHome on _GraphHomeScreenState {
  String? _sceneScope(SurfaceComponent? component) {
    if (component == null) return null;
    if (component.kind == 'brain-graph') return component.properties['scope'];
    for (final child in component.children) {
      final scope = _sceneScope(child);
      if (scope != null) return scope;
    }
    return null;
  }

  void _applySceneScope() {
    final scope = _sceneScope(widget.surfaceRoot);
    _allActivities = scope == 'all';
    _systemGraph = scope == 'system';
  }

  void _scheduleResults({bool force = false}) {
    final activity = _focusedActivity;
    if (activity == null || widget.onReadActivityResults == null) return;
    if (!force &&
        _resultVersions[activity.id] == activity.version &&
        _activityResults.containsKey(activity.id)) {
      return;
    }
    if (!force &&
        _resultsFor == activity.id &&
        _resultsVersion == activity.version &&
        _resultsLoading) {
      return;
    }
    _resultsTimer?.cancel();
    final request = ++_resultsRequest;
    final id = activity.id, version = activity.version;
    _resultsFor = id;
    _resultsVersion = version;
    _resultsError = null;
    _resultsLoading = true;
    _resultsTimer = Timer(const Duration(milliseconds: 200), () async {
      try {
        final results = await widget.onReadActivityResults!(id);
        if (!mounted || request != _resultsRequest || _selectedActivity != id) {
          return;
        }
        _update(() {
          _activityResults[id] = results
              .where((turn) => turn.correlationId == activity.correlationId)
              .toList();
          _resultVersions[id] = version;
          _resultsLoading = false;
          _resultsError = null;
        });
      } catch (_) {
        if (mounted && request == _resultsRequest && _selectedActivity == id) {
          _update(() {
            _resultsLoading = false;
            _resultsError = 'Cannot load this activity’s results.';
          });
        }
      }
    });
  }

  String _eventKey(ExecutionActivityEvent event) =>
      '${event.operationId}:${event.signalId}:${event.phase}:${event.timestamp}';
  void _connectActivities() {
    final watch = widget.onWatchActivities;
    if (watch == null) return;
    _activityStream = watch().listen(
      (items) {
        if (!mounted) return;
        _update(() {
          final previous = {
            for (final activity in _activities) activity.id: activity,
          };
          _activityPulses = {};
          if (_receivedActivities && _activityFailure == null) {
            for (final activity in items) {
              final known =
                  previous[activity.id]?.events.map(_eventKey).toSet() ??
                  <String>{};
              _activityPulses[activity.id] = activity.events
                  .where((event) => !known.contains(_eventKey(event)))
                  .toList();
            }
          }
          _receivedActivities = true;
          _activities = items;
          _activityFailure = null;
          if (_selectedCommand != null) {
            for (final activity in items) {
              if (activity.commandId == _selectedCommand) {
                _selectedActivity = activity.id;
                _localActivity = null;
                _localTitle = null;
              }
            }
          }
        });
        _pulseClear?.cancel();
        _pulseClear = Timer(const Duration(milliseconds: 1400), () {
          if (mounted) _update(() => _activityPulses = {});
        });
        _scheduleResults();
      },
      onError: (Object error) => _activitiesDisconnected(),
      onDone: _activitiesDisconnected,
    );
  }

  void _activitiesDisconnected() {
    if (!mounted || _activityRetry?.isActive == true) return;
    _update(() => _activityFailure = 'Reconnecting activities…');
    unawaited(_activityStream?.cancel());
    _activityRetry = Timer(const Duration(seconds: 3), _connectActivities);
  }

  ExecutionActivity? get _focusedActivity {
    for (final activity in _activities) {
      if (activity.id == _selectedActivity) return activity;
    }
    return null;
  }

  void _selectActivity(ExecutionActivity activity) => _update(() {
    _selectedActivity = activity.id;
    _selectedCommand = activity.commandId;
    _localActivity = null;
    _localTitle = null;
    _selected = null;
    _directory = false;
    _systemGraph = false;
    _allActivities = false;
    _activityPulses = {};
    _scheduleResults();
  });

  Widget _composedHome(SurfaceComponent component) => Material(
    key: const Key('activity_home'),
    color: LumenPalette.background,
    child: _renderComponent(component),
  );

  Widget _renderComponent(SurfaceComponent component) =>
      switch (component.kind) {
        'split' => _componentSplit(component),
        'brain-graph' => _activityGraph(component),
        'chat' => _activityChat(component),
        'activity-list' => _taskManager(),
        _ => Center(
          child: Text('Component “${component.kind}” is not installed.'),
        ),
      };

  Widget _componentSplit(SurfaceComponent component) {
    if (component.children.isEmpty) return const SizedBox.shrink();
    if (component.children.length == 1) {
      return _renderComponent(component.children.single);
    }
    if (component.children.length != 2) {
      return const Center(child: Text('A split needs two components.'));
    }
    final id = component.key ?? 'split';
    final graphFraction =
        double.tryParse(component.properties['graphFraction'] ?? '') ?? .5;
    final initial = component.children.last.kind == 'brain-graph'
        ? 1 - graphFraction
        : graphFraction;
    return LayoutBuilder(
      builder: (context, constraints) {
        final vertical = constraints.maxWidth < 780;
        final fraction = (_splitFractions[id] ?? initial).clamp(.28, .72);
        final first = Expanded(
          flex: (fraction * 1000).round(),
          child: _renderComponent(component.children.first),
        );
        final second = Expanded(
          flex: ((1 - fraction) * 1000).round(),
          child: _renderComponent(component.children.last),
        );
        if (vertical && _editingBehavior != null) {
          final chat = component.children
              .where((child) => child.kind == 'chat')
              .firstOrNull;
          if (chat != null) return _renderComponent(chat);
        }
        if (vertical) {
          return Column(
            key: const Key('activity_split'),
            children: [first, const Divider(height: 1), second],
          );
        }
        return Row(
          key: const Key('activity_split'),
          children: [
            first,
            Semantics(
              label: 'Resize graph and input',
              child: MouseRegion(
                cursor: SystemMouseCursors.resizeColumn,
                child: GestureDetector(
                  onHorizontalDragUpdate: (details) => _update(() {
                    _splitFractions[id] =
                        (fraction + details.delta.dx / constraints.maxWidth)
                            .clamp(.28, .72);
                  }),
                  child: Container(
                    width: 7,
                    color: LumenPalette.line,
                    child: Center(
                      child: Container(
                        width: 2,
                        height: 30,
                        color: LumenPalette.muted.withValues(alpha: .4),
                      ),
                    ),
                  ),
                ),
              ),
            ),
            second,
          ],
        );
      },
    );
  }

  Widget _activityGraph(SurfaceComponent component) {
    final full = _brain.snapshot;
    final focus = _focusedActivity;
    final visible = _systemGraph
        ? full
        : full == null
        ? null
        : activityCanvas(
            full,
            _allActivities
                ? _activities
                : focus == null
                ? const []
                : [focus],
          );
    final panel = component.children
        .where((c) => c.kind == 'activity-list')
        .firstOrNull;
    final pulses = _allActivities
        ? _activityPulses.values.expand((events) => events).toList()
        : _activityPulses[_selectedActivity] ?? <ExecutionActivityEvent>[];
    final activityIds = pulses
        .expand(
          (event) => [
            event.sourceNeuronId,
            if (event.targetNeuronId != null) event.targetNeuronId!,
          ],
        )
        .toSet();
    final edgeIds =
        visible?.synapses
            .where(
              (edge) => pulses.any(
                (event) =>
                    event.sourceNeuronId == edge.sourceId &&
                    event.targetNeuronId == edge.targetId &&
                    (event.signalType == edge.signalType ||
                        edge.signalType == '*'),
              ),
            )
            .map((edge) => edge.id)
            .toSet() ??
        <String>{};
    final inspect = full == null || visible == null
        ? full
        : BrainSnapshot(
            rootId: full.rootId,
            observedAt: full.observedAt,
            scope: full.scope,
            nodes: {
              for (final node in [...full.nodes, ...visible.nodes])
                node.id: node,
            }.values.toList(),
            synapses: {
              for (final edge in [...full.synapses, ...visible.synapses])
                edge.id: edge,
            }.values.toList(),
            activity: visible.activity,
            correlations: visible.correlations,
            truncated: visible.truncated,
          );
    return Column(
      key: const Key('graph_brain_panel'),
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 12, 8),
          child: Row(
            children: [
              const Icon(
                Icons.hub_outlined,
                size: 17,
                color: LumenPalette.accent,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  _systemGraph
                      ? 'System'
                      : _allActivities
                      ? 'All activities'
                      : focus?.title ?? 'Your brain',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              if (widget.behaviorStudio != null)
                IconButton(
                  key: const Key('behavior_library'),
                  tooltip: 'Behaviors',
                  icon: const Icon(Icons.code, size: 18),
                  onPressed: () => _update(() {
                    _systemGraph = true;
                    _directory = true;
                    _selected = null;
                  }),
                ),
              IconButton(
                tooltip: 'System graph',
                isSelected: _systemGraph,
                icon: const Icon(Icons.account_tree_outlined, size: 18),
                onPressed: () => _update(() => _systemGraph = !_systemGraph),
              ),
            ],
          ),
        ),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) => Stack(
              children: [
                Positioned.fill(
                  child: full == null
                      ? _emptyGraph()
                      : visible!.nodes.isEmpty
                      ? Center(
                          child: Padding(
                            padding: const EdgeInsets.all(24),
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                const InoPresence(size: 64),
                                const SizedBox(height: 16),
                                Text(
                                  _selectedCommand != null
                                      ? 'Waiting for this activity’s graph…'
                                      : 'Start something. Watch it happen.',
                                  textAlign: TextAlign.center,
                                  style: const TextStyle(
                                    fontFamily: 'Georgia',
                                    fontSize: 24,
                                    color: LumenPalette.ink,
                                  ),
                                ),
                                const SizedBox(height: 8),
                                const Text(
                                  'Send a message, select an activity, or open System.',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(
                                    fontSize: 12,
                                    color: LumenPalette.muted,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        )
                      : LumenBrainGraph(
                          snapshot: visible,
                          selectedId: _selected,
                          activeNodes: _systemGraph
                              ? _brain.activeNodes
                              : activityIds,
                          activeEdges: _systemGraph
                              ? _brain.activeEdges
                              : edgeIds,
                          stale: _brain.stale,
                          onNeuron: (node) => _update(() {
                            _selected = node.id;
                            _directory = false;
                          }),
                          onSynapse: (edge) => _update(() {
                            _selected = edge.id;
                            _directory = false;
                          }),
                          onActivity: (event) => _update(() {
                            _selected = 'operation:${event.operationId}';
                            _directory = false;
                          }),
                        ),
                ),
                if (panel != null)
                  Positioned(
                    top: 8,
                    right: 12,
                    width: constraints.maxWidth < 420 ? 210 : 248,
                    child: ConstrainedBox(
                      constraints: BoxConstraints(
                        maxHeight: (constraints.maxHeight * .48).clamp(
                          100,
                          310,
                        ),
                      ),
                      child: _taskManager(),
                    ),
                  ),
                if (_selected != null || _directory)
                  Positioned(
                    top: 8,
                    bottom: 8,
                    left: 12,
                    width: (constraints.maxWidth - 24).clamp(200, 350),
                    child: _inspector(inspect),
                  ),
              ],
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 6, 16, 10),
          child: Row(
            children: [
              Icon(
                _brain.stale ? Icons.cloud_off : Icons.circle,
                size: 7,
                color: _brain.stale ? LumenPalette.muted : LumenPalette.accent,
              ),
              const SizedBox(width: 7),
              Expanded(
                child: Text(
                  _brain.failure ??
                      (full == null
                          ? 'Connecting…'
                          : 'Live · ${_systemGraph
                                ? 'current topology'
                                : _allActivities
                                ? 'all observed activities'
                                : 'selected activity'}${full.truncated ? ' · limited history' : ''}'),
                  style: const TextStyle(
                    fontSize: 10,
                    color: LumenPalette.muted,
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _taskManager() => DecoratedBox(
    key: const Key('activity_task_manager'),
    decoration: BoxDecoration(
      color: LumenPalette.surface,
      border: Border.all(color: LumenPalette.line),
      borderRadius: BorderRadius.circular(12),
      boxShadow: [
        BoxShadow(color: Colors.black.withValues(alpha: .025), blurRadius: 16),
      ],
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 7, 4, 4),
          child: Row(
            children: [
              const Expanded(
                child: Text(
                  'Activities',
                  style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                ),
              ),
              FilterChip(
                key: const Key('all_activities'),
                label: const Text('All', style: TextStyle(fontSize: 10)),
                tooltip: 'Show all activities on graph',
                selected: _allActivities,
                onSelected: (value) => _update(() {
                  _allActivities = value;
                  _systemGraph = false;
                }),
                visualDensity: VisualDensity.compact,
              ),
            ],
          ),
        ),
        if (_activityFailure != null)
          Padding(
            padding: const EdgeInsets.all(8),
            child: Text(
              _activityFailure!,
              style: const TextStyle(fontSize: 10, color: LumenPalette.muted),
            ),
          ),
        Flexible(
          child: ListView(
            shrinkWrap: true,
            padding: const EdgeInsets.fromLTRB(4, 0, 4, 5),
            children: [
              if (_localTitle != null)
                ListTile(
                  dense: true,
                  title: const Text(
                    'UserMessaged',
                    style: TextStyle(fontSize: 10, color: LumenPalette.muted),
                  ),
                  subtitle: Text(
                    _localTitle!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                  trailing: const Icon(Icons.more_horiz, size: 15),
                ),
              for (final activity in _activities)
                Material(
                  color: _selectedActivity == activity.id
                      ? LumenPalette.accentSoft
                      : Colors.transparent,
                  borderRadius: BorderRadius.circular(7),
                  child: InkWell(
                    key: ValueKey('execution_activity_${activity.id}'),
                    borderRadius: BorderRadius.circular(7),
                    onTap: () => _selectActivity(activity),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 9,
                        vertical: 8,
                      ),
                      child: Row(
                        children: [
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  activity.triggerName,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 10,
                                    color: LumenPalette.muted,
                                  ),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  activity.title,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 12,
                                    color: LumenPalette.ink,
                                    fontWeight: FontWeight.w500,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          const SizedBox(width: 6),
                          Tooltip(
                            message: activity.status,
                            child: Icon(
                              switch (activity.status) {
                                'running' => Icons.more_horiz,
                                'waiting' => Icons.front_hand_outlined,
                                'completed' => Icons.check,
                                'failed' => Icons.error_outline,
                                'cancelled' => Icons.stop_circle_outlined,
                                _ => Icons.visibility_outlined,
                              },
                              size: 14,
                              color: activity.status == 'failed'
                                  ? LumenPalette.error
                                  : LumenPalette.accent,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              if (_activities.isEmpty && _localTitle == null)
                const Padding(
                  padding: EdgeInsets.all(10),
                  child: Text(
                    'No activities yet',
                    style: TextStyle(fontSize: 11, color: LumenPalette.muted),
                  ),
                ),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _activityChat(SurfaceComponent component) {
    final transport = component.properties['name'] ?? widget.chatName;
    if (transport != widget.chatName) {
      return Center(child: Text('Input “$transport” is not connected.'));
    }
    final chat = BrainChatScreen(
      key: _chatKey,
      chatName: transport,
      turns: widget.turns,
      activityMode: true,
      activityCorrelationId: _focusedActivity?.correlationId,
      activityCommandId: _selectedCommand,
      activityLocalId: _localActivity,
      onActivityStarted: (id, title) => _update(() {
        _localActivity = id;
        _localTitle = title;
        _selectedActivity = null;
        _selectedCommand = null;
        _allActivities = false;
        _systemGraph = false;
      }),
      onActivityAccepted: (id, commandId) => _update(() {
        if (_localActivity == id) {
          _selectedCommand = commandId;
          for (final activity in _activities) {
            if (activity.commandId == commandId) {
              _selectedActivity = activity.id;
              _localActivity = null;
              _localTitle = null;
            }
          }
          _scheduleResults();
        }
      }),
      activityTurns: _activityResults.values
          .expand((turns) => turns)
          .toList(growable: false),
      onSend: widget.onSend,
      onStream: widget.onStream,
      onStreamVoice: component.properties['voice'] == 'false'
          ? null
          : widget.onStreamVoice,
      onAttachmentTap: widget.onAttachmentTap,
      onOpenSignIn: widget.onOpenSignIn,
      kernelBaseUri: widget.kernelBaseUri,
      onCancelTurn: widget.onCancelTurn,
      onReadChart: widget.onReadChart,
      onReadImageBytes: widget.onReadImageBytes,
      onReadSpreadsheet: widget.onReadSpreadsheet,
      onReadGraph: widget.onReadGraph,
    );
    return Column(
      key: const Key('graph_chat_panel'),
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 13, 12, 8),
          child: Row(
            children: [
              const InoPresence(size: 28),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  _editingBehavior == null ? 'With Ino' : _editingBehavior!,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              if (_editingBehavior != null)
                TextButton(
                  onPressed: () => _update(() => _editingBehavior = null),
                  child: const Text('Back to input'),
                )
              else
                IconButton(
                  tooltip: 'New activity',
                  icon: const Icon(Icons.add, size: 18),
                  onPressed: () => _update(() {
                    _selectedActivity = null;
                    _selectedCommand = null;
                    _localActivity = null;
                    _localTitle = null;
                    _selected = null;
                  }),
                ),
            ],
          ),
        ),
        Expanded(
          child: IndexedStack(
            index: _editingBehavior == null
                ? 0
                : _openBehaviors.indexOf(_editingBehavior!) + 1,
            children: [
              Column(
                children: [
                  if (_resultsFor == _selectedActivity && _resultsLoading)
                    const LinearProgressIndicator(minHeight: 2),
                  if (_resultsFor == _selectedActivity && _resultsError != null)
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              _resultsError!,
                              style: const TextStyle(
                                fontSize: 12,
                                color: LumenPalette.error,
                              ),
                            ),
                          ),
                          TextButton(
                            onPressed: () =>
                                _update(() => _scheduleResults(force: true)),
                            child: const Text('Retry'),
                          ),
                        ],
                      ),
                    ),
                  if (_selectedActivity != null &&
                      _activityResults.containsKey(_selectedActivity) &&
                      !_activityResults[_selectedActivity]!.any(
                        (turn) =>
                            !turn.fromUser && turn.signal != 'TurnLifecycle',
                      ))
                    const Padding(
                      padding: EdgeInsets.all(12),
                      child: Text(
                        'No output yet.',
                        style: TextStyle(
                          fontSize: 12,
                          color: LumenPalette.muted,
                        ),
                      ),
                    ),
                  Expanded(child: chat),
                ],
              ),
              if (widget.behaviorStudio != null)
                for (final name in _openBehaviors)
                  BehaviorEditor(
                    key: ValueKey(name),
                    api: widget.behaviorStudio!,
                    name: name,
                    changes: _brain,
                    readGraph: () => _brain.snapshot,
                  ),
            ],
          ),
        ),
      ],
    );
  }
}
