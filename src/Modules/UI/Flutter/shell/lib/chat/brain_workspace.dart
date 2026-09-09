import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../activity_screen.dart';
import '../onboarding/onboarding_screen.dart';
import '../user_actions/user_action_card.dart';
import '../windowing/windowing_screen.dart';

import 'chat_contracts.dart';
import 'brain_graph_store.dart';
import 'graph_home_screen.dart';
import 'workspace_chrome.dart';
import 'workspace_session.dart';

final class BrainWorkspace extends StatefulWidget {
  const BrainWorkspace({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.onReadSurface,
    this.onReadBrain,
    this.onWatchBrain,
    this.behaviorStudio,
    this.onSetBrainSubscription,
    this.graphSceneFactory,
    this.userActions = const [],
    this.statusMessage,
    this.onWatchActivities,
    this.surfaceEvents,
    this.client,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final ReadSurface? onReadSurface;
  final ReadBrain? onReadBrain;
  final WatchBrain? onWatchBrain;
  final ApplicationStudioApi? behaviorStudio;
  final SetBrainSubscription? onSetBrainSubscription;
  final GraphSceneFactory? graphSceneFactory;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;
  final WatchExecutionActivities? onWatchActivities;
  final Stream<SceneOpenedEvent>? surfaceEvents;
  final DigitalBrainUiClient? client;

  @override
  State<BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  late final WorkspaceSession _session;
  BrainGraphStore? _graph;
  int _destination = graphDestinationIndex;
  KitSurfaceScene? _scene;
  String? _surfaceFailure;
  bool _surfaceLoading = true, _settings = false, _oldUi = false;
  StreamSubscription<SceneOpenedEvent>? _surfaceEvents;
  int _surfaceReadVersion = 0;

  @override
  void initState() {
    super.initState();
    _session = WorkspaceSession(chatName: widget.chatName, turns: widget.turns)
      ..addListener(_onSession);
    _attachGraph();
    unawaited(_readSurface());
    _listenSurface();
  }

  void _attachGraph() {
    _graph?.removeListener(_onSession);
    _graph?.dispose();
    if (widget.onReadBrain == null && widget.onWatchBrain == null) {
      _graph = null;
      return;
    }
    _graph = BrainGraphStore(
      read: widget.onReadBrain,
      watch: widget.onWatchBrain,
      setSubscription: widget.onSetBrainSubscription,
    )..addListener(_onSession);
  }

  void _onSession() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void didUpdateWidget(covariant BrainWorkspace oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.onReadSurface != widget.onReadSurface) {
      unawaited(_readSurface());
    }
    if (!identical(oldWidget.surfaceEvents, widget.surfaceEvents)) {
      _listenSurface();
    }
    if (oldWidget.chatName != widget.chatName) {
      _session.updateChatName(widget.chatName);
    }
    if (!identical(oldWidget.turns, widget.turns)) {
      _session.listenTurns(widget.turns);
    }
    if (oldWidget.onReadBrain != widget.onReadBrain ||
        oldWidget.onWatchBrain != widget.onWatchBrain ||
        oldWidget.onSetBrainSubscription != widget.onSetBrainSubscription) {
      _attachGraph();
    }
  }

  void _selectDestination(int index) {
    if (_destination != index) {
      setState(() => _destination = index);
    }
  }

  @override
  void dispose() {
    unawaited(_surfaceEvents?.cancel());
    _graph?.removeListener(_onSession);
    _graph?.dispose();
    _session
      ..removeListener(_onSession)
      ..dispose();
    super.dispose();
  }

  Widget _destinationPage() => IndexedStack(
    index: _destination == 0 || _destination == graphDestinationIndex ? 0 : 1,
    children: [
      GraphHomeScreen(
        chatName: widget.chatName,
        turns: _session.projectedTurns,
        conversation: _destination == 0,
        onSend: widget.onSend,
        onStream: widget.onStream,
        onStreamVoice: widget.onStreamVoice,
        onAttachmentTap: widget.onAttachmentTap,
        onOpenSignIn: widget.onOpenSignIn,
        kernelBaseUri: widget.kernelBaseUri,
        onCancelTurn: widget.onCancelTurn,
        onReadChart: widget.onReadChart,
        onReadImageBytes: widget.onReadImageBytes,
        onReadSpreadsheet: widget.onReadSpreadsheet,
        onReadGraph: widget.onReadGraph,
        onReadBrain: widget.onReadBrain,
        onWatchBrain: widget.onWatchBrain,
        behaviorStudio: widget.behaviorStudio,
        onSetBrainSubscription: widget.onSetBrainSubscription,
        sceneFactory: widget.graphSceneFactory,
        graph: _graph,
      ),
      Theme(
        data: KitTheme.dark(),
        child: ColoredBox(
          color: KitPalette.surface,
          child: switch (_destination) {
            onboardingDestinationIndex => const OnboardingScreen(),
            activityDestinationIndex => ActivityScreen(
              turns: _session.projectedTurns,
              correlations: _graph?.snapshot?.correlations ?? const [],
              truncated: _graph?.snapshot?.truncated ?? false,
              userActions: widget.userActions,
              onOpenUserAction: widget.onOpenSignIn,
            ),
            kitDestinationIndex => const KitGalleryScreen(),
            windowingDestinationIndex => WindowingScreen(
              onReadSurface: widget.onReadSurface,
            ),
            _ => const SizedBox.shrink(),
          },
        ),
      ),
    ],
  );
  @override
  Widget build(BuildContext context) {
    if (!_oldUi) return _scriptedWorkspace();
    final status = _session.statusMessage(widget.statusMessage);
    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < _compactBreakpoint;
        final content = Column(
          children: [
            TextButton.icon(
              onPressed: () => setState(() => _oldUi = false),
              icon: const Icon(Icons.arrow_back, size: 16),
              label: const Text('Back to Home'),
            ),
            WorkspaceStatusBar(
              chatName: widget.chatName,
              section: workspaceSectionName(_destination),
              message: status,
            ),
            Expanded(child: _destinationPage()),
          ],
        );

        return Scaffold(
          body: compact
              ? content
              : Row(
                  children: [
                    WorkspaceRail(
                      selectedIndex: _destination,
                      onSelected: _selectDestination,
                    ),

                    Expanded(child: content),
                  ],
                ),
          bottomNavigationBar: compact
              ? WorkspaceNavigationBar(
                  selectedIndex: _destination,
                  onSelected: _selectDestination,
                )
              : null,
        );
      },
    );
  }

  void _listenSurface() {
    unawaited(_surfaceEvents?.cancel());
    _surfaceEvents = widget.surfaceEvents?.listen(
      (_) => unawaited(_readSurface()),
      onError: (Object error) {
        if (mounted) {
          setState(() => _surfaceFailure = 'Surface updates disconnected.');
        }
      },
    );
  }

  Future<void> _readSurface() async {
    final version = ++_surfaceReadVersion;
    try {
      final surface = await widget.onReadSurface?.call('desk');
      if (!mounted || version != _surfaceReadVersion) return;
      setState(() {
        _scene = surface?.scenes
            .where((scene) => scene.surfaceKey == 'home')
            .firstOrNull;
        _surfaceLoading = false;
        _surfaceFailure = _scene?.root == null
            ? 'Home has not been opened by the UI startup script.'
            : null;
      });
      final input = _findInput(_scene?.root);
      if (widget.client != null &&
          input != null &&
          input != _session.chatName) {
        _session.updateChatName(input);
        _session.listenTurns(widget.client!.watchChatTurns(chatName: input));
      }
    } catch (_) {
      if (mounted && version == _surfaceReadVersion) {
        setState(() {
          _surfaceLoading = false;
          _surfaceFailure =
              'Cannot read the Home surface. Reconnect and retry.';
        });
      }
    }
  }

  String? _findInput(SurfaceComponent? root) {
    if (root == null) return null;
    if (root.kind == 'chat') return root.properties['name'] ?? widget.chatName;
    for (final child in root.children) {
      final name = _findInput(child);
      if (name != null) return name;
    }
    return null;
  }

  Widget _scriptedWorkspace() => Scaffold(
    backgroundColor: LumenPalette.background,
    body: Column(
      children: [
        Container(
          height: 56,
          decoration: const BoxDecoration(
            border: Border(bottom: BorderSide(color: LumenPalette.line)),
          ),
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: Row(
            children: [
              const Icon(
                Icons.hub_outlined,
                color: LumenPalette.accent,
                size: 22,
              ),
              const SizedBox(width: 9),
              const Text(
                'digitalbrain',
                style: TextStyle(
                  color: LumenPalette.ink,
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const Spacer(),
              if (widget.statusMessage != null)
                Flexible(
                  child: Text(
                    widget.statusMessage!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontSize: 11,
                      color: LumenPalette.muted,
                    ),
                  ),
                ),
              IconButton(
                key: const Key('home_settings'),
                tooltip: _settings ? 'Back to Home' : 'Settings',
                icon: Icon(
                  _settings ? Icons.close : Icons.settings_outlined,
                  size: 20,
                ),
                onPressed: () => setState(() => _settings = !_settings),
              ),
            ],
          ),
        ),
        Expanded(
          child: IndexedStack(
            index: _settings ? 1 : 0,
            children: [
              _scene?.root == null
                  ? Center(
                      child: Padding(
                        padding: const EdgeInsets.all(28),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Text(
                              _surfaceLoading
                                  ? 'Opening Home…'
                                  : _surfaceFailure ?? 'Home is unavailable.',
                              textAlign: TextAlign.center,
                            ),
                            const SizedBox(height: 12),
                            if (!_surfaceLoading)
                              TextButton(
                                onPressed: _readSurface,
                                child: const Text('Retry'),
                              ),
                          ],
                        ),
                      ),
                    )
                  : GraphHomeScreen(
                      key: const Key('scripted_home_scene'),
                      chatName: _session.chatName,
                      turns: _session.projectedTurns,
                      surfaceRoot: _scene!.root,
                      surfaceKey: _scene!.surfaceKey,
                      onActivateControl: widget.client == null
                          ? null
                          : (surfaceKey, controlId, intent) =>
                                widget.client!.activateControl(
                                  surfaceName: 'desk',
                                  surfaceKey: surfaceKey,
                                  controlId: controlId,
                                  intent: intent,
                                ),
                      onWatchActivities: widget.onWatchActivities,
                      onReadActivityResults: widget.client == null
                          ? null
                          : (id) => widget.client!.readActivityResults(
                              surfaceName: 'desk',
                              activityId: id,
                            ),
                      onSend: widget.onSend,
                      onStream: widget.client == null
                          ? widget.onStream
                          : (text) => widget.client!.streamMessage(
                              chatName: _session.chatName,
                              text: text,
                            ),
                      onStreamVoice: widget.client == null
                          ? widget.onStreamVoice
                          : (bytes, {fileName = 'voice.wav'}) =>
                                widget.client!.streamVoice(
                                  chatName: _session.chatName,
                                  audioBytes: bytes,
                                  fileName: fileName,
                                ),
                      onAttachmentTap: widget.onAttachmentTap,
                      onOpenSignIn: widget.onOpenSignIn,
                      kernelBaseUri: widget.kernelBaseUri,
                      onCancelTurn: widget.client == null
                          ? widget.onCancelTurn
                          : ({required commandId, required turnId}) =>
                                widget.client!.cancelTurn(
                                  chatName: _session.chatName,
                                  commandId: commandId,
                                  turnId: turnId,
                                ),
                      onReadChart: widget.onReadChart,
                      onReadImageBytes: widget.onReadImageBytes,
                      onReadSpreadsheet: widget.onReadSpreadsheet,
                      onReadGraph: widget.onReadGraph,
                      onReadBrain: widget.onReadBrain,
                      onWatchBrain: widget.onWatchBrain,
                      behaviorStudio: widget.behaviorStudio,
                      onSetBrainSubscription: widget.onSetBrainSubscription,
                      sceneFactory: widget.graphSceneFactory,
                      graph: _graph,
                    ),
              Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 620),
                  child: ListView(
                    padding: const EdgeInsets.all(28),
                    children: [
                      const Text(
                        'Settings',
                        style: TextStyle(fontFamily: 'Georgia', fontSize: 32),
                      ),
                      const SizedBox(height: 24),
                      ListTile(
                        key: const Key('settings_ui_kit'),
                        leading: const Icon(Icons.widgets_outlined),
                        title: const Text('UI kit'),
                        subtitle: const Text(
                          'Components, examples, and states',
                        ),
                        onTap: () => Navigator.of(context).push(
                          MaterialPageRoute<void>(
                            builder: (_) => Scaffold(
                              appBar: AppBar(title: const Text('UI kit')),
                              body: const KitGalleryScreen(),
                            ),
                          ),
                        ),
                      ),
                      const Divider(),
                      ListTile(
                        key: const Key('settings_old_ui'),
                        leading: const Icon(Icons.desktop_windows_outlined),
                        title: const Text('OldUI'),
                        subtitle: const Text(
                          'Previous graph, conversation, activity, and windowing views',
                        ),
                        onTap: () => setState(() {
                          _oldUi = true;
                          _settings = false;
                        }),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}
