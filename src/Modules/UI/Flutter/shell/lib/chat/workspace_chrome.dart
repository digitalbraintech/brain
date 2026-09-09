import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

const _destinations = <(String, IconData, String)>[
  ('Conversation', Icons.chat_bubble_outline_rounded, 'chat'),
  ('Getting started', Icons.auto_stories_outlined, 'onboarding'),
  ('My brain', Icons.hub_outlined, 'graph'),
  ('Activity', Icons.timeline_rounded, 'activity'),
  ('UI kit', Icons.widgets_outlined, 'kit'),
  ('Workspace', Icons.desktop_windows_outlined, 'windowing'),
];

final class WorkspaceRail extends StatelessWidget {
  const WorkspaceRail({
    super.key,
    required this.selectedIndex,
    required this.onSelected,
  });
  final int selectedIndex;
  final ValueChanged<int> onSelected;
  @override
  Widget build(BuildContext context) => Container(
    width: 206,
    decoration: const BoxDecoration(
      color: Color(0xfff0f2eb),
      border: Border(right: BorderSide(color: LumenPalette.line)),
    ),
    child: SafeArea(
      child: LayoutBuilder(
        builder: (context, constraints) => SingleChildScrollView(
          child: ConstrainedBox(
            constraints: BoxConstraints(minHeight: constraints.maxHeight),
            child: IntrinsicHeight(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(17, 27, 17, 20),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Row(
                      children: [
                        BrainMark(),
                        SizedBox(width: 9),
                        Expanded(
                          child: Text(
                            'digitalbrain',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 19,
                              letterSpacing: -.6,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                      ],
                    ),
                    const Padding(
                      padding: EdgeInsets.fromLTRB(43, 2, 0, 34),
                      child: Text(
                        'with Ino',
                        style: TextStyle(
                          fontSize: 11,
                          color: LumenPalette.muted,
                        ),
                      ),
                    ),
                    const Padding(
                      padding: EdgeInsets.only(left: 12, bottom: 14),
                      child: Text(
                        'YOUR SPACE',
                        style: TextStyle(
                          fontSize: 9,
                          letterSpacing: 2,
                          color: LumenPalette.muted,
                        ),
                      ),
                    ),
                    for (final index in [2, 0, 3]) _destination(index),
                    const SizedBox(height: 32),
                    const Padding(
                      padding: EdgeInsets.only(left: 12, bottom: 14),
                      child: Text(
                        'EXPLORE',
                        style: TextStyle(
                          fontSize: 9,
                          letterSpacing: 2,
                          color: LumenPalette.muted,
                        ),
                      ),
                    ),
                    for (final index in [1, 4, 5]) _destination(index),
                    const Spacer(),
                    const LumenSurface(
                      elevated: false,
                      padding: EdgeInsets.all(15),
                      radius: 17,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Icon(
                            Icons.route_outlined,
                            size: 21,
                            color: LumenPalette.accent,
                          ),
                          SizedBox(height: 10),
                          Text(
                            'Connected, by you.',
                            style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          SizedBox(height: 6),
                          Text(
                            'Inspect any neuron to see its activity and manage subscriptions.',
                            style: TextStyle(
                              fontSize: 11,
                              color: LumenPalette.muted,
                              height: 1.5,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 17),
                    const Row(
                      children: [
                        CircleAvatar(
                          radius: 13,
                          backgroundColor: LumenPalette.accentSoft,
                          child: Text(
                            'Y',
                            style: TextStyle(
                              fontSize: 10,
                              color: LumenPalette.accent,
                            ),
                          ),
                        ),
                        SizedBox(width: 9),
                        Expanded(
                          child: Text(
                            'Your personal space',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 10,
                              color: LumenPalette.muted,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );
  Widget _destination(int index) => Padding(
    padding: const EdgeInsets.only(bottom: 5),
    child: Material(
      color: selectedIndex == index
          ? LumenPalette.accentSoft
          : Colors.transparent,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        key: Key('destination_${_destinations[index].$3}'),
        onTap: () => onSelected(index),
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 13, vertical: 13),
          child: Row(
            children: [
              Icon(
                _destinations[index].$2,
                size: 18,
                color: selectedIndex == index
                    ? LumenPalette.accent
                    : LumenPalette.muted,
              ),
              const SizedBox(width: 11),
              Expanded(
                child: Text(
                  _destinations[index].$1,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: selectedIndex == index
                        ? FontWeight.w600
                        : FontWeight.w400,
                    color: selectedIndex == index
                        ? LumenPalette.accent
                        : LumenPalette.muted,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

final class WorkspaceNavigationBar extends StatelessWidget {
  const WorkspaceNavigationBar({
    super.key,
    required this.selectedIndex,
    required this.onSelected,
  });
  final int selectedIndex;
  final ValueChanged<int> onSelected;
  @override
  Widget build(BuildContext context) => SafeArea(
    top: false,
    child: Container(
      height: 62,
      decoration: const BoxDecoration(
        color: LumenPalette.surface,
        border: Border(top: BorderSide(color: LumenPalette.line)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceAround,
        children: [
          for (final index in [2, 0, 3])
            Expanded(
              child: InkWell(
                key: Key('destination_${_destinations[index].$3}'),
                onTap: () => onSelected(index),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(
                      _destinations[index].$2,
                      size: 21,
                      color: selectedIndex == index
                          ? LumenPalette.accent
                          : LumenPalette.muted,
                    ),
                    const SizedBox(height: 3),
                    Text(
                      _destinations[index].$1,
                      style: const TextStyle(fontSize: 10),
                    ),
                  ],
                ),
              ),
            ),
          PopupMenuButton<int>(
            tooltip: 'More destinations',
            icon: const Icon(Icons.more_horiz),
            onSelected: onSelected,
            itemBuilder: (_) => [
              for (final index in [1, 4, 5])
                PopupMenuItem(
                  value: index,
                  child: Text(_destinations[index].$1),
                ),
            ],
          ),
        ],
      ),
    ),
  );
}

final class BrainMark extends StatelessWidget {
  const BrainMark({super.key});
  @override
  Widget build(BuildContext context) => Container(
    width: 33,
    height: 33,
    decoration: BoxDecoration(
      color: LumenPalette.accent,
      borderRadius: BorderRadius.circular(11),
    ),
    child: const Icon(Icons.blur_on_rounded, color: Colors.white, size: 24),
  );
}

final class WorkspaceStatusBar extends StatelessWidget {
  const WorkspaceStatusBar({
    super.key,
    required this.chatName,
    required this.section,
    this.message,
  });
  final String chatName, section;
  final String? message;
  @override
  Widget build(BuildContext context) => Container(
    height: 64,
    padding: const EdgeInsets.symmetric(horizontal: 24),
    decoration: const BoxDecoration(
      border: Border(bottom: BorderSide(color: LumenPalette.line)),
    ),
    child: Row(
      children: [
        Text(
          section,
          style: const TextStyle(fontSize: 12, color: LumenPalette.ink),
        ),
        const SizedBox(width: 12),
        const Text('/', style: TextStyle(color: LumenPalette.lineStrong)),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            chatName,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontSize: 12, color: LumenPalette.muted),
          ),
        ),
        Container(
          width: 6,
          height: 6,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: message == null ? LumenPalette.accent : LumenPalette.warning,
          ),
        ),
        const SizedBox(width: 7),
        Text(
          message == null ? 'Personal workspace' : 'not connected',
          style: const TextStyle(fontSize: 10, color: LumenPalette.muted),
        ),
      ],
    ),
  );
}

String workspaceSectionName(int index) => _destinations[index].$1;
