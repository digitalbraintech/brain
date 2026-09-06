import 'dart:convert';
import 'dart:typed_data';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:forui/forui.dart';
import '../chat/kit_chat.dart';
import '../components/button/kit_button.dart';
import '../components/card/kit_card.dart';
import '../components/chart/kit_chart.dart';
import '../components/clock/kit_clock.dart';
import '../components/graph/graph_models.dart';
import '../components/graph/kit_graph.dart';
import '../components/graph/kit_graph_controller.dart';
import '../components/graph/kit_graph_navigator.dart';
import '../components/graph/kit_graph_view.dart';
import '../components/image/kit_image.dart';
import '../components/sheet/kit_sheet.dart';
import '../components/view/kit_view.dart';
import '../lumen/ino_presence.dart';
import '../lumen/lumen_brain_graph.dart';
import '../lumen/lumen_controls.dart';
import '../lumen/lumen_palette.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';

final class GalleryEntry {
  const GalleryEntry(
    this.id,
    this.title,
    this.category,
    this.icon,
    this.description,
    this.api,
    this.notes, [
    this.states = const ['Normal'],
    this.dark = false,
  ]);
  final String id, title, category, description, api, notes;
  final IconData icon;
  final List<String> states;
  final bool dark;
}

const galleryEntries = [
  GalleryEntry(
    'lumen',
    'Lumen foundations',
    'Foundations',
    Icons.palette_outlined,
    'Quiet cream surfaces, botanical greens, and controls shared across the product.',
    'LumenPalette · LumenSurface\nLumenActionButton · LumenIconButton\nLumenTextField · KitMarkdown',
    'The Lumen palette is the product foundation. Actions, icon buttons and fields use Forui for focus, keyboard and disabled behavior.',
    ['Normal', 'Disabled'],
  ),
  GalleryEntry(
    'forui-buttons',
    'Forui buttons',
    'Foundations',
    Icons.ads_click,
    'Primary, secondary, outline, ghost and destructive actions.',
    'FButton · FButton.icon · FButtonVariant',
    'A null onPress disables the actual control; loading uses an explicit progress prefix.',
    ['Normal', 'Loading', 'Disabled'],
  ),
  GalleryEntry(
    'forui-forms',
    'Forui form controls',
    'Foundations',
    Icons.tune,
    'Text input, checkbox and switch with local values and validation examples.',
    'FTextField · FTextFieldControl\nFCheckbox · FSwitch',
    'Error and disabled modes use the controls’ own properties. All entered values remain local.',
    ['Normal', 'Error', 'Disabled'],
  ),
  GalleryEntry(
    'feedback',
    'Alerts & progress',
    'Foundations',
    Icons.info_outline,
    'Accessible status feedback and indeterminate progress.',
    'FAlert · FCircularProgress',
    'Loading is indeterminate, with no invented completion percentage. Error uses the destructive alert variant.',
    ['Normal', 'Loading', 'Error'],
  ),
  GalleryEntry(
    'presence',
    'Ino presence',
    'Foundations',
    Icons.face_outlined,
    'The shared Ino face expresses observed activity and attention.',
    'InoPresence(state: InoPresenceState.…)',
    'These presentation examples do not start work or record audio. The component respects reduced motion.',
    [
      'Idle',
      'Listening',
      'Working',
      'Waiting',
      'Completed',
      'Attention',
      'Disconnected',
    ],
  ),
  GalleryEntry(
    'button',
    'Action button',
    'Inputs & actions',
    Icons.smart_button_outlined,
    'A portable action for surfaces and rich messages.',
    'KitButton(part: KitButtonPart(…), onPressed: …)',
    'The counter demonstrates a local action. A host can also observe the optional onButtonPressed callback.',
    ['Normal', 'Disabled'],
  ),
  GalleryEntry(
    'view',
    'Interactive view',
    'Inputs & actions',
    Icons.calculate_outlined,
    'A kind-bound surface with a calculator keypad and spreadsheet view.',
    'KitView(kind: …, display: …, phase: …, onKey: …)',
    'Keys update the local display and report the pressed key. Calculation belongs to the host; this preview does not evaluate expressions.',
    ['Normal', 'Disabled', 'Spreadsheet'],
    true,
  ),
  GalleryEntry(
    'chat',
    'Chat & rich text',
    'Content',
    Icons.chat_bubble_outline,
    'The exported chat surface with copyable messages and a local composer.',
    'KitChat(chatController: …, currentUserId: …, resolveUser: …)',
    'Send a sample message to append it locally. No model or transport is connected. Empty clears the sample history.',
    ['Normal', 'Empty'],
  ),
  GalleryEntry(
    'card',
    'Content card',
    'Content',
    Icons.article_outlined,
    'A title, body and labeled fields for compact structured results.',
    'KitCard(part: KitCardPart(…))',
    'Compact omits optional fields. This component has no loading or error contract.',
    ['Normal', 'Compact'],
    true,
  ),
  GalleryEntry(
    'image',
    'Image',
    'Content',
    Icons.image_outlined,
    'An in-memory image with a caption and built-in decode error fallback.',
    'KitImage(bytes: …, caption: …)',
    'Normal uses a tiny PNG fixture; error supplies invalid bytes to exercise the real decoder fallback. Decode loading is transient and is not forced.',
    ['Normal', 'Error'],
    true,
  ),
  GalleryEntry(
    'chart',
    'Bar chart',
    'Data & time',
    Icons.bar_chart,
    'A shared series chart for surfaces and rich message content.',
    'KitChart(part: KitChartPart(title: …, points: …))',
    'Empty supplies no points and shows the component’s own “No series” state. This component currently renders bars.',
    ['Normal', 'Empty'],
    true,
  ),
  GalleryEntry(
    'sheet',
    'Spreadsheet',
    'Data & time',
    Icons.table_chart_outlined,
    'A horizontally and vertically scrollable data table.',
    'KitSheet(part: KitSheetPart(…))',
    'Empty retains column headers with no rows. Wide adds columns to exercise horizontal scrolling.',
    ['Normal', 'Empty', 'Wide'],
    true,
  ),
  GalleryEntry(
    'clock',
    'Clock & countdown',
    'Data & time',
    Icons.schedule,
    'A wall clock and timer display backed by the local clock.',
    'KitClock(part: KitTimerPart(label: …, dueAt: …))',
    'Countdown starts at one minute on selection. Expired shows zero. This display does not schedule a backend timer.',
    ['Wall clock', 'Countdown', 'Expired'],
    true,
  ),
  GalleryEntry(
    'graph',
    'Projected graph',
    'Graphs',
    Icons.hub_outlined,
    'A depth-projected graph with draggable rotation and inspectable nodes.',
    'KitGraph(nodes: …, edges: …, onNodeTap: …)',
    'Drag to rotate and select a sample node or edge. Empty provides no nodes or links.',
    ['Normal', 'Empty'],
    true,
  ),
  GalleryEntry(
    'brain-graph',
    'Lumen brain graph',
    'Graphs',
    Icons.account_tree_outlined,
    'The product’s pannable whiteboard of neurons and signal relationships.',
    'LumenBrainGraph(snapshot: …, onNeuron: …, onSynapse: …)',
    'This is a fixture snapshot. Select a neuron or relationship to inspect it locally. Stale uses the renderer’s disconnected presentation.',
    ['Normal', 'Empty', 'Stale'],
  ),
  GalleryEntry(
    'graph-3d',
    'Spatial graph',
    'Graphs',
    Icons.view_in_ar_outlined,
    'The WebGL graph and its shared navigation controller.',
    'KitGraphView(controller: …)\nKitGraphNavigator(controller: …)',
    'Launch the actual renderer on demand. Drag to orbit, scroll to zoom, and select nodes to focus. Requires a graphics-capable host; no replacement renderer is used.',
  ),
];

final class GalleryPreview extends StatefulWidget {
  const GalleryPreview({super.key, required this.entry, this.onButtonPressed});
  final GalleryEntry entry;
  final ValueChanged<KitButtonPart>? onButtonPressed;
  @override
  State<GalleryPreview> createState() => _GalleryPreviewState();
}

final class _GalleryPreviewState extends State<GalleryPreview> {
  late String _state = widget.entry.states.first;
  final _draft = TextEditingController(text: 'A little room to think');
  final _chat = InMemoryChatController(
    messages: const [
      TextMessage(
        id: 'example-1',
        authorId: 'owner',
        text: 'Show me a calmer workspace.',
      ),
      TextMessage(
        id: 'example-2',
        authorId: 'ino',
        text: 'A clear surface, a small next step, and room for what matters.',
      ),
    ],
  );
  final _graph = KitGraphController(nodes: _nodes, edges: _edges);
  bool _checked = true, _switched = false, _launched = false;
  int _count = 0;
  String _display = '42', _lastAction = 'Select a sample to inspect it.';
  DateTime _due = DateTime.now().toUtc().add(const Duration(minutes: 1));
  static const _nodes = [
    GraphNode(id: 'ino', label: 'Ino', kind: GraphNodeKind.hub),
    GraphNode(id: 'memory', label: 'Memory'),
    GraphNode(id: 'view', label: 'View'),
  ];
  static const _edges = [
    GraphEdge(id: 'ino-memory', sourceId: 'ino', targetId: 'memory'),
    GraphEdge(
      id: 'ino-view',
      sourceId: 'ino',
      targetId: 'view',
      decorated: true,
    ),
  ];
  static final _png = base64Decode(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=',
  );
  @override
  void dispose() {
    _draft.dispose();
    _chat.dispose();
    _graph.dispose();
    super.dispose();
  }

  void _change(String value) {
    setState(() {
      _state = value;
      _due = DateTime.now().toUtc().add(const Duration(minutes: 1));
    });
    if (widget.entry.id == 'chat') {
      _chat.setMessages(
        value == 'Empty'
            ? []
            : const [
                TextMessage(
                  id: 'example-2',
                  authorId: 'ino',
                  text:
                      'A clear surface, a small next step, and room for what matters.',
                ),
              ],
      );
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final state in widget.entry.states)
            ChoiceChip(
              key: Key('gallery_state_$state'),
              label: Text(state),
              selected: state == _state,
              onSelected: (_) => _change(state),
              selectedColor: LumenPalette.accentSoft,
              showCheckmark: false,
            ),
        ],
      ),
      const SizedBox(height: 16),
      Container(
        clipBehavior: Clip.antiAlias,
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(18),
          border: Border.all(color: LumenPalette.line),
          color: widget.entry.dark ? KitPalette.surface : LumenPalette.surface,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
              color: LumenPalette.surfaceMuted,
              child: Row(
                children: [
                  const Icon(
                    Icons.science_outlined,
                    size: 16,
                    color: LumenPalette.muted,
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      widget.entry.dark
                          ? 'LIVE PREVIEW · LEGACY DARK COMPONENT'
                          : 'LIVE PREVIEW · LOCAL SAMPLE',
                      style: const TextStyle(
                        color: LumenPalette.muted,
                        fontSize: 10,
                        letterSpacing: 0.8,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            Padding(padding: const EdgeInsets.all(20), child: _example()),
          ],
        ),
      ),
    ],
  );
  Widget _stack(List<Widget> children) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      for (var i = 0; i < children.length; i++) ...[
        if (i > 0) const SizedBox(height: 18),
        children[i],
      ],
    ],
  );
  void _action() => setState(() => _count++);
  bool get _disabled => _state == 'Disabled';
  void _key(String key) => setState(() {
    _lastAction = 'Pressed $key';
    if (key == 'C' || key == 'CE') {
      _display = '0';
    } else if (key == 'BS') {
      _display = _display.length <= 1
          ? '0'
          : _display.substring(0, _display.length - 1);
    } else if (RegExp(r'^[0-9.]$').hasMatch(key) && _display.length < 12) {
      _display = _display == '0' ? key : '$_display$key';
    }
  });
  Widget _example() {
    switch (widget.entry.id) {
      case 'lumen':
        return _stack([
          Wrap(
            spacing: 10,
            runSpacing: 10,
            children: [
              for (final token in [
                ('Cream', LumenPalette.background),
                ('Surface', LumenPalette.surface),
                ('Green', LumenPalette.accent),
                ('Soft green', LumenPalette.accentSoft),
                ('Ink', LumenPalette.ink),
              ])
                SizedBox(
                  width: 84,
                  child: Column(
                    children: [
                      Container(
                        height: 50,
                        decoration: BoxDecoration(
                          color: token.$2,
                          borderRadius: BorderRadius.circular(10),
                          border: Border.all(color: LumenPalette.line),
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(token.$1, style: const TextStyle(fontSize: 11)),
                    ],
                  ),
                ),
            ],
          ),
          const KitMarkdown(
            '### Room for what matters\nA readable type scale, **clear emphasis**, and a quiet surface.',
          ),
          LumenSurface(
            selected: _checked,
            elevated: false,
            child: const Text('A selected surface'),
          ),
          LumenTextField(
            controller: _draft,
            enabled: !_disabled,
            hint: 'Write a thought',
            maxLines: 2,
          ),
          Wrap(
            spacing: 10,
            runSpacing: 10,
            children: [
              LumenActionButton(
                label: 'Try an action',
                primary: true,
                onPressed: _disabled ? null : _action,
              ),
              LumenIconButton(
                icon: const Icon(Icons.bookmark_outline),
                label: 'Toggle selection',
                selected: _checked,
                onPressed: _disabled
                    ? null
                    : () => setState(() => _checked = !_checked),
              ),
            ],
          ),
          Text('Local actions: $_count'),
        ]);
      case 'forui-buttons':
        return _stack([
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: [
              for (final variant in [
                FButtonVariant.primary,
                FButtonVariant.secondary,
                FButtonVariant.outline,
                FButtonVariant.ghost,
                FButtonVariant.destructive,
              ])
                FButton(
                  variant: variant,
                  mainAxisSize: MainAxisSize.min,
                  onPress: _disabled || _state == 'Loading' ? null : _action,
                  prefix: _state == 'Loading'
                      ? const FCircularProgress()
                      : null,
                  child: Text(switch (variant) {
                    FButtonVariant.primary => 'Primary',
                    FButtonVariant.secondary => 'Secondary',
                    FButtonVariant.outline => 'Outline',
                    FButtonVariant.ghost => 'Ghost',
                    _ => 'Destructive',
                  }),
                ),
            ],
          ),
          Text('Local actions: $_count'),
        ]);
      case 'forui-forms':
        return _stack([
          FTextField(
            control: FTextFieldControl.managed(controller: _draft),
            label: const Text('Sample title'),
            hint: 'Give it a name',
            enabled: !_disabled,
            error: _state == 'Error'
                ? const Text('Choose a more descriptive title.')
                : null,
          ),
          FCheckbox(
            label: const Text('Include a summary'),
            value: _checked,
            enabled: !_disabled,
            onChange: (value) => setState(() => _checked = value),
            error: _state == 'Error' ? const Text('Review this choice.') : null,
          ),
          FSwitch(
            label: const Text('Quiet mode'),
            value: _switched,
            enabled: !_disabled,
            onChange: (value) => setState(() => _switched = value),
          ),
        ]);
      case 'feedback':
        return _state == 'Loading'
            ? const Row(
                children: [
                  FCircularProgress(),
                  SizedBox(width: 14),
                  Expanded(child: Text('Loading preview…')),
                ],
              )
            : FAlert(
                variant: _state == 'Error'
                    ? FAlertVariant.destructive
                    : FAlertVariant.primary,
                title: Text(
                  _state == 'Error'
                      ? 'Something needs attention'
                      : 'Ready for the next step',
                ),
                subtitle: const Text('This is an example of status feedback.'),
              );
      case 'presence':
        return Center(
          child: InoPresence(
            size: 140,
            state: switch (_state) {
              'Listening' => InoPresenceState.listening,
              'Working' => InoPresenceState.working,
              'Waiting' => InoPresenceState.waiting,
              'Completed' => InoPresenceState.completed,
              'Attention' => InoPresenceState.attention,
              'Disconnected' => InoPresenceState.disconnected,
              _ => InoPresenceState.idle,
            },
          ),
        );
      case 'button':
        return _stack([
          KitButton(
            part: const KitButtonPart(
              buttonId: 'gallery-action',
              label: 'Try this action',
              action: 'gallery-example',
            ),
            onPressed: _disabled
                ? null
                : (part) {
                    _action();
                    widget.onButtonPressed?.call(part);
                  },
          ),
          Text('Action invoked $_count ${_count == 1 ? 'time' : 'times'}'),
        ]);
      case 'card':
        return KitCard(
          part: KitCardPart(
            title: 'A good week',
            body: 'Small steps added up.',
            fields: _state == 'Compact'
                ? const []
                : const [
                    (label: 'Focus sessions', value: '12'),
                    (label: 'Time outside', value: '4 hours'),
                  ],
          ),
        );
      case 'chart':
        return KitChart(
          part: KitChartPart(
            title: 'Weekly rhythm',
            points: _state == 'Empty'
                ? const []
                : const [
                    KitChartPoint(label: 'Mon', value: 4),
                    KitChartPoint(label: 'Tue', value: 7),
                    KitChartPoint(label: 'Wed', value: 5),
                    KitChartPoint(label: 'Thu', value: 9),
                    KitChartPoint(label: 'Fri', value: 6),
                  ],
          ),
        );
      case 'image':
        return KitImage(
          bytes: _state == 'Error' ? Uint8List.fromList([0, 1, 2]) : _png,
          caption: 'Local PNG decode fixture',
        );
      case 'sheet':
        return KitSheet(
          part: KitSheetPart(
            title: 'A small reading list',
            sheetName: 'Books',
            columns: _state == 'Wide'
                ? const ['Title', 'Author', 'Format', 'Pages', 'Shelf']
                : const ['Title', 'Status'],
            rows: _state == 'Empty'
                ? const []
                : _state == 'Wide'
                ? const [
                    [
                      'The Creative Act',
                      'Rick Rubin',
                      'Hardcover',
                      '432',
                      'Ideas',
                    ],
                    [
                      'A Field Guide',
                      'Local sample',
                      'Paperback',
                      '128',
                      'Nature',
                    ],
                  ]
                : const [
                    ['The Creative Act', 'Reading'],
                    ['A Field Guide', 'Next'],
                  ],
          ),
        );
      case 'view':
        return _stack([
          KitView(
            kind: _state == 'Spreadsheet' ? 'spreadsheet' : 'calculator',
            display: _display,
            onKey: _disabled ? null : _key,
          ),
          Text(_lastAction, style: KitType.bodyMuted),
        ]);
      case 'clock':
        return Center(
          child: KitClock(
            key: ValueKey(_state),
            part: _state == 'Wall clock'
                ? null
                : KitTimerPart(
                    label: _state == 'Expired'
                        ? 'Expired sample'
                        : 'One minute sample',
                    dueAt: _state == 'Expired' ? DateTime.utc(2000) : _due,
                  ),
          ),
        );
      case 'chat':
        return SizedBox(
          height: 420,
          child: KitChat(
            chatController: _chat,
            currentUserId: 'owner',
            resolveUser: (id) async =>
                User(id: id, name: id == 'ino' ? 'Ino' : 'You'),
            onMessageSend: (text) => _chat.insertMessage(
              TextMessage(
                id: 'local-${_count++}',
                authorId: 'owner',
                text: text,
              ),
            ),
          ),
        );
      case 'graph':
        return _stack([
          SizedBox(
            height: 320,
            child: KitGraph(
              nodes: _state == 'Empty' ? const [] : _nodes,
              edges: _state == 'Empty' ? const [] : _edges,
              onNodeTap: (node) =>
                  setState(() => _lastAction = 'Selected ${node.label}'),
              onEdgeTap: (edge) => setState(
                () => _lastAction = '${edge.sourceId} → ${edge.targetId}',
              ),
            ),
          ),
          Text(_lastAction, style: KitType.bodyMuted),
        ]);
      case 'brain-graph':
        return _stack([
          SizedBox(
            height: 380,
            child: LumenBrainGraph(
              snapshot: BrainSnapshot(
                rootId: 'ino',
                observedAt: DateTime.utc(2026, 1, 1),
                nodes: _state == 'Empty'
                    ? const []
                    : const [
                        BrainNeuron(
                          id: 'ino',
                          type: 'assistant',
                          name: 'ino',
                          label: 'Ino',
                          module: 'AI',
                        ),
                        BrainNeuron(
                          id: 'memory',
                          type: 'memory',
                          name: 'memory',
                          label: 'Memory',
                          module: 'Memory',
                        ),
                        BrainNeuron(
                          id: 'view',
                          type: 'view',
                          name: 'view',
                          label: 'View',
                          module: 'UI',
                        ),
                      ],
                synapses: _state == 'Empty'
                    ? const []
                    : const [
                        BrainSynapse(
                          id: 'ino-memory',
                          sourceId: 'ino',
                          targetId: 'memory',
                          signalType: 'SampleSignal',
                          kind: 'Subscription',
                        ),
                      ],
              ),
              stale: _state == 'Stale',
              onNeuron: (node) =>
                  setState(() => _lastAction = 'Selected ${node.label}'),
              onSynapse: (edge) =>
                  setState(() => _lastAction = edge.signalType),
            ),
          ),
          Text(_lastAction),
        ]);
      case 'graph-3d':
        return !_launched
            ? _stack([
                const Icon(
                  Icons.view_in_ar_outlined,
                  size: 60,
                  color: LumenPalette.accent,
                ),
                const Text(
                  'Explore a spatial network',
                  textAlign: TextAlign.center,
                ),
                Center(
                  child: LumenActionButton(
                    label: 'Launch 3D preview',
                    primary: true,
                    onPressed: () => setState(() => _launched = true),
                  ),
                ),
              ])
            : SizedBox(
                height: 380,
                child: ColoredBox(
                  color: KitPalette.surface,
                  child: Column(
                    children: [
                      Expanded(child: KitGraphView(controller: _graph)),
                      KitGraphNavigator(controller: _graph),
                    ],
                  ),
                ),
              );
      default:
        return const SizedBox.shrink();
    }
  }
}
