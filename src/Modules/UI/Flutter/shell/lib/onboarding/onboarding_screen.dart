import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';

final class OnboardingScreen extends StatefulWidget {
  const OnboardingScreen({super.key});

  @override
  State<OnboardingScreen> createState() => _OnboardingScreenState();
}

final class _OnboardingScreenState extends State<OnboardingScreen> {
  OnboardingLessonPlayer? _player;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_player != null) {
      return;
    }
    final animate = !MediaQuery.disableAnimationsOf(context);
    _player = OnboardingLessonPlayer(animate: animate)
      ..addListener(_onPlayer)
      ..start();
  }

  void _onPlayer() {
    if (mounted) {
      setState(() {});
    }
  }

  @override
  void dispose() {
    _player
      ?..removeListener(_onPlayer)
      ..dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final player = _player;
    if (player == null) {
      return const SizedBox.shrink();
    }
    final frame = player.frame;
    final compact = MediaQuery.sizeOf(context).width < 720;
    final graph = ColoredBox(
      color: BrainPalette.surfaceSunken,
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: BrainPalette.surface,
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: BrainPalette.line),
          ),
          child: KitGraph(
            nodes: frame.nodes,
            edges: frame.edges,
            pulse: frame.pulse,
            highlightEdgeId: frame.highlightEdgeId,
          ),
        ),
      ),
    );
    final rule = _RulePanel(
      capability: player.capability,
      onReplay: player.replay,
    );

    return ColoredBox(
      key: const Key('onboarding_screen'),
      color: BrainPalette.surface,
      child: Column(
        children: [
          OnboardingCapabilityRail(
            selectedId: player.capability.id,
            onSelected: player.select,
          ),
          const Divider(height: 1),
          Expanded(
            child: compact
                ? Column(
                    children: [
                      Expanded(flex: 3, child: graph),
                      Expanded(flex: 2, child: rule),
                    ],
                  )
                : Row(
                    children: [
                      Expanded(flex: 3, child: graph),
                      const VerticalDivider(width: 1, thickness: 1),
                      SizedBox(width: 340, child: rule),
                    ],
                  ),
          ),
        ],
      ),
    );
  }
}

final class _RulePanel extends StatelessWidget {
  const _RulePanel({required this.capability, required this.onReplay});

  final OnboardingCapability capability;
  final VoidCallback onReplay;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 18, 20, 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(capability.title, style: BrainType.heading),
          const SizedBox(height: 6),
          Text(capability.blurb, style: BrainType.metaStrong),
          const SizedBox(height: 16),
          Expanded(
            child: SingleChildScrollView(
              child: Text(capability.rule, style: BrainType.body),
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('onboarding_replay'),
            onPressed: onReplay,
            icon: const Icon(Icons.replay, size: 16),
            label: const Text('Replay'),
          ),
        ],
      ),
    );
  }
}
