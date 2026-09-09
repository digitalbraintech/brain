import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';
import 'package:provider/provider.dart';

final class VoiceComposerController extends ChangeNotifier {
  bool recording = false;
  bool busy = false;
  final draft = TextEditingController();
  final focus = FocusNode();

  void update({bool? recording, bool? busy}) {
    final nextRecording = recording ?? this.recording;
    final nextBusy = busy ?? this.busy;
    if (nextRecording == this.recording && nextBusy == this.busy) {
      return;
    }
    this.recording = nextRecording;
    this.busy = nextBusy;
    notifyListeners();
  }

  @override
  void dispose() {
    draft.dispose();
    focus.dispose();
    super.dispose();
  }
}

final class BrainChatComposer extends StatefulWidget {
  const BrainChatComposer({
    super.key,
    required this.canVoice,
    required this.onVoiceTap,
    this.embedded = false,
    this.onSend,
    this.onAttachmentTap,
  });

  final bool canVoice;
  final VoidCallback onVoiceTap;
  final bool embedded;
  final OnMessageSendCallback? onSend;
  final VoidCallback? onAttachmentTap;

  @override
  State<BrainChatComposer> createState() => _BrainChatComposerState();
}

final class _SendChatIntent extends Intent {
  const _SendChatIntent();
}

final class _BrainChatComposerState extends State<BrainChatComposer> {
  final _measureKey = GlobalKey();
  late final TextEditingController _text;
  late final ValueNotifier<bool> _hasText;

  @override
  void initState() {
    super.initState();
    _text = context.read<VoiceComposerController>().draft;
    _hasText = ValueNotifier(_text.text.trim().isNotEmpty);
    _text.addListener(_onText);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void didUpdateWidget(covariant BrainChatComposer oldWidget) {
    super.didUpdateWidget(oldWidget);
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  @override
  void dispose() {
    _text.removeListener(_onText);
    _hasText.dispose();
    super.dispose();
  }

  void _onText() {
    _hasText.value = _text.text.trim().isNotEmpty;
    WidgetsBinding.instance.addPostFrameCallback((_) => _measure());
  }

  OnMessageSendCallback? get _onSend =>
      widget.embedded ? widget.onSend : context.read<OnMessageSendCallback?>();

  @override
  Widget build(BuildContext context) {
    final onSend = _onSend;
    final onAttach = widget.embedded
        ? widget.onAttachmentTap
        : context.read<OnAttachmentTapCallback?>();
    final bottomSafe = widget.embedded
        ? 0.0
        : MediaQuery.paddingOf(context).bottom;
    final body = Container(
      key: _measureKey,
      padding: EdgeInsets.fromLTRB(12, 10, 12, 10 + bottomSafe),
      decoration: BoxDecoration(
        color: LumenPalette.surface,
        border: Border.all(color: LumenPalette.line),
        borderRadius: widget.embedded
            ? const BorderRadius.vertical(bottom: Radius.circular(20))
            : BorderRadius.circular(20),
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              LumenIconButton(
                key: const Key('chat_attach_button'),
                label: 'Attach',
                icon: const Icon(Icons.add_rounded, size: 20),
                onPressed: onAttach,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Shortcuts(
                  shortcuts: const {
                    SingleActivator(LogicalKeyboardKey.enter):
                        _SendChatIntent(),
                  },
                  child: Actions(
                    actions: {
                      _SendChatIntent: CallbackAction<_SendChatIntent>(
                        onInvoke: (_) {
                          _submit(_text.text);
                          return null;
                        },
                      ),
                    },
                    child: LumenTextField(
                      key: const Key('chat_composer_input'),
                      controller: _text,
                      focusNode: context.read<VoiceComposerController>().focus,
                      hint: onSend == null
                          ? 'Connect to start a conversation'
                          : 'Ask Ino anything…',
                      minLines: 1,
                      maxLines: 3,
                      enabled: onSend != null,
                      onSubmitted: _submit,
                    ),
                  ),
                ),
              ),
              if (widget.canVoice) ...[
                const SizedBox(width: 8),
                _VoiceButton(onTap: widget.onVoiceTap),
              ],
              const SizedBox(width: 8),
              ValueListenableBuilder<bool>(
                valueListenable: _hasText,
                builder: (context, hasText, _) => LumenIconButton(
                  key: const Key('chat_send_button'),
                  label: 'Send',
                  primary: true,
                  icon: const Icon(Icons.arrow_upward_rounded, size: 19),
                  onPressed: hasText && onSend != null
                      ? () => _submit(_text.text)
                      : null,
                ),
              ),
            ],
          ),
          if (onSend != null) ...[
            const SizedBox(height: 8),
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _AssistantHint(
                    label: 'Personal code review',
                    prompt: 'Review my local repository diff. Focus on correctness, concurrency, and durable state. Give actionable findings with file and line references; skip cosmetic comments.',
                    onSend: onSend,
                  ),
                  _AssistantHint(
                    label: 'My behaviors',
                    prompt: 'List my saved C# behaviors and explain what each one does.',
                    onSend: onSend,
                  ),
                  _AssistantHint(
                    label: 'Create a behavior',
                    prompt: 'Help me turn a routine into a C# behavior. Ask what should trigger it and what it should do.',
                    onSend: onSend,
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
    if (widget.embedded) return body;
    return Positioned(left: 12, right: 12, bottom: 12, child: body);
  }

  void _submit(String text) {
    final trimmed = text.trim();
    final onSend = _onSend;
    if (trimmed.isEmpty || onSend == null) return;
    onSend(trimmed);
    _text.clear();
  }

  void _measure() {
    if (!mounted || widget.embedded) return;
    final box = _measureKey.currentContext?.findRenderObject() as RenderBox?;
    if (box == null) return;
    context.read<ComposerHeightNotifier>().setHeight(
      box.size.height + 24 - MediaQuery.paddingOf(context).bottom,
    );
  }
}

final class _AssistantHint extends StatelessWidget {
  const _AssistantHint({
    required this.label,
    required this.prompt,
    required this.onSend,
  });
  final String label;
  final String prompt;
  final OnMessageSendCallback onSend;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(right: 6),
    child: LumenActionButton(
      key: Key('assistant_hint_${label.toLowerCase().replaceAll(' ', '_')}'),
      label: label,
      onPressed: () => onSend(prompt),
    ),
  );
}

final class _VoiceButton extends StatelessWidget {
  const _VoiceButton({required this.onTap});
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final voice = context.watch<VoiceComposerController>();
    return LumenIconButton(
      key: const Key('chat_voice_button'),
      label: voice.recording
          ? 'Stop and send'
          : voice.busy
          ? 'Sending voice'
          : 'Record voice',
      selected: voice.recording,
      icon: Icon(
        voice.recording
            ? Icons.stop_circle_outlined
            : voice.busy
            ? Icons.hourglass_top_rounded
            : Icons.mic_none_rounded,
        size: 19,
      ),
      onPressed: voice.busy ? null : onTap,
    );
  }
}
