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
  final BehaviorStudioApi? behaviorStudio;
  final SetBrainSubscription? onSetBrainSubscription;
  final GraphSceneFactory? graphSceneFactory;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;

  @override
  State<BrainWorkspace> createState() => _BrainWorkspaceState();
}

final class _BrainWorkspaceState extends State<BrainWorkspace> {
  static const _compactBreakpoint = 720.0;

  late final WorkspaceSession _session;
  BrainGraphStore? _graph;
  int _destination = graphDestinationIndex;

  @override
  void initState() {
    super.initState();
    _session = WorkspaceSession(chatName: widget.chatName, turns: widget.turns)
      ..addListener(_onSession);
    _attachGraph();
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
    final status = _session.statusMessage(widget.statusMessage);
    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < _compactBreakpoint;
        final content = Column(
          children: [
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
}
