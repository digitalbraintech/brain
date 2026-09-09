import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'brain_chat_screen.dart';
import 'brain_graph_simulation.dart';
import 'chat_contracts.dart';

/// A real assistant conversation beside an illustrative, modular brain graph.
final class GraphExamplesScreen extends StatefulWidget {
  const GraphExamplesScreen({
    super.key,
    required this.chatName,
    required this.turns,
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
    this.sceneFactory,
  });

  final String chatName;
  final List<ChatTurnEvent> turns;
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
  final GraphSceneFactory? sceneFactory;

  @override
  State<GraphExamplesScreen> createState() => _GraphExamplesScreenState();
}

final class _GraphExamplesScreenState extends State<GraphExamplesScreen> {
  final _simulation = BrainGraphSimulation();
  late final _graph = KitGraphController(
    nodes: BrainGraphSimulation.nodes,
    edges: _simulation.edges,
    camera: GraphCamera(
      initial: const GraphCameraState(yaw: 0, pitch: -0.02, zoom: 1.15),
    ),
  );
  bool _bound = false;

  @override
  void initState() {
    super.initState();
    _simulation.addListener(_onSimulation);
  }

  void _onSimulation() {
    final bound = _simulation.current?.bound == true;
    if (bound != _bound) {
      _bound = bound;
      _graph.setGraph(
        nodes: BrainGraphSimulation.nodes,
        edges: _simulation.edges,
      );
    }
    setState(() {});
  }

  @override
  void dispose() {
    _simulation.removeListener(_onSimulation);
    _simulation.dispose();
    _graph.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Material(
    key: const Key('graph_home_screen'),
    color: BrainPalette.surfaceSunken,
    child: LayoutBuilder(
      builder: (context, constraints) {
        final chat = _chat();
        final graph = _brain();
        if (constraints.maxWidth < 820) {
          return Column(
            children: [
              Expanded(flex: 3, child: chat),
              const Divider(height: 1),
              Expanded(flex: 5, child: graph),
            ],
          );
        }
        return Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SizedBox(
              width: (constraints.maxWidth * 0.34).clamp(350.0, 420.0),
              child: chat,
            ),
            const VerticalDivider(width: 1, thickness: 1),
            Expanded(child: graph),
          ],
        );
      },
    ),
  );

  Widget _chat() => ColoredBox(
    key: const Key('graph_chat_panel'),
    color: BrainPalette.surface,
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Padding(
          padding: EdgeInsets.fromLTRB(20, 18, 20, 14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Assistant', style: BrainType.title),
              SizedBox(height: 4),
              Text(
                'Your conversation, alongside the brain.',
                style: BrainType.bodyMuted,
              ),
            ],
          ),
        ),
        const Divider(height: 1),
        Expanded(
          child: BrainChatScreen(
            chatName: widget.chatName,
            turns: widget.turns,
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
          ),
        ),
      ],
    ),
  );

  Widget _brain() => Padding(
    key: const Key('graph_brain_panel'),
    padding: const EdgeInsets.all(16),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            const Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('DigitalBrain', style: BrainType.heading),
                  SizedBox(height: 3),
                  Text(
                    'Modules contain neurons. Synapses carry signals.',
                    style: BrainType.bodyMuted,
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
              decoration: BoxDecoration(
                color: BrainPalette.signal.withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(20),
              ),
              child: Text(
                'SIMULATION',
                style: BrainType.metaStrong.copyWith(
                  color: BrainPalette.signal,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 14),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final example in BrainGraphExample.values)
              Tooltip(
                message: example.description,
                child: ActionChip(
                  key: Key('graph_example_${example.name}'),
                  avatar: Icon(
                    _simulation.example == example && _simulation.playing
                        ? Icons.graphic_eq
                        : Icons.play_arrow,
                    size: 17,
                  ),
                  label: Text(example.label),
                  backgroundColor: _simulation.example == example
                      ? BrainPalette.signal.withValues(alpha: 0.12)
                      : BrainPalette.surfaceRaised,
                  onPressed: () => _simulation.play(example),
                ),
              ),
          ],
        ),
        const SizedBox(height: 12),
        Expanded(
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: BrainPalette.surfaceSunken,
              borderRadius: BorderRadius.circular(16),
              border: Border.all(color: BrainPalette.line),
            ),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(16),
              child: Stack(
                fit: StackFit.expand,
                children: [
                  KitGraphView(
                    controller: _graph,
                    sceneFactory: widget.sceneFactory,
                    pulse: _simulation.pulse,
                    showLabels: true,
                    semanticsLabel: 'DigitalBrain 3D simulation. UI, AI, Kernel and Time modules contain seven neurons. Drag to orbit and scroll to zoom.',
                  ),
                  const Positioned(
                    left: 14,
                    top: 12,
                    child: Text(
                      '4 MODULES  /  7 NEURONS',
                      style: BrainType.meta,
                    ),
                  ),
                  const Positioned(
                    left: 14,
                    bottom: 12,
                    child: Text(
                      'Drag to orbit · Scroll to zoom',
                      style: BrainType.meta,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
        const SizedBox(height: 10),
        Wrap(
          spacing: 14,
          runSpacing: 4,
          children: [
            _legend(BrainPalette.owner, 'Learned synapse'),
            _legend(BrainPalette.success, 'Bound subscription'),
            _legend(const Color(0xFFFFF0BD), 'Signal'),
          ],
        ),
        const SizedBox(height: 10),
        _playback(),
      ],
    ),
  );

  Widget _legend(Color color, String label) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 7,
        height: 7,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 6),
      Text(label, style: BrainType.meta),
    ],
  );

  Widget _playback() {
    final step = _simulation.current;
    return Container(
      key: const Key('graph_simulation_status'),
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: BrainPalette.surfaceRaised,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  step == null
                      ? 'Play an example'
                      : '${_simulation.stepIndex + 1}/${_simulation.steps.length} · ${step.title}',
                  style: BrainType.cardTitle,
                ),
              ),
              if (step != null) ...[
                IconButton(
                  key: const Key('graph_simulation_pause'),
                  visualDensity: VisualDensity.compact,
                  tooltip: _simulation.playing
                      ? 'Pause simulation'
                      : _simulation.complete
                      ? 'Replay simulation'
                      : 'Resume simulation',
                  onPressed: _simulation.togglePause,
                  icon: Icon(
                    _simulation.playing
                        ? Icons.pause
                        : _simulation.complete
                        ? Icons.replay
                        : Icons.play_arrow,
                    size: 19,
                  ),
                ),
                IconButton(
                  key: const Key('graph_simulation_reset'),
                  visualDensity: VisualDensity.compact,
                  tooltip: 'Reset simulation',
                  onPressed: _simulation.reset,
                  icon: const Icon(Icons.stop, size: 19),
                ),
              ],
            ],
          ),
          const SizedBox(height: 6),
          Text(
            step?.detail ?? 'Play a local example of signal delivery or subscription changes. Synapses are illustrative; this is not a live topology. Chat uses your assistant.',
            style: BrainType.bodyMuted,
          ),
          if (step != null) ...[
            const SizedBox(height: 10),
            LinearProgressIndicator(
              value: (_simulation.stepIndex + 1) / _simulation.steps.length,
              minHeight: 2,
            ),
          ],
        ],
      ),
    );
  }
}
