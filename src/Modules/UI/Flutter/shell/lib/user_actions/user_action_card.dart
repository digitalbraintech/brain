import 'package:flutter/material.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';

import '../brain_theme.dart';

final class UserActionCardModel {
  const UserActionCardModel({
    required this.moduleId,
    required this.displayText,
    required this.actionUrl,
    required this.taskId,
    this.continuationState = 'waiting',
    this.displayName,
    this.actionLabel = 'Connect / Authorize',
    this.statusText,
  });

  final String moduleId;
  final String displayText;
  final Uri actionUrl;
  final String taskId;
  final String continuationState;
  final String? displayName;
  final String actionLabel;
  final String? statusText;
}

final class UserActionCard extends StatelessWidget {
  const UserActionCard({
    super.key,
    required this.model,
    this.onAuthorize,
    this.onCancel,
    this.leading,
    this.authorizeButton,
    this.showCancel = false,
  });

  final UserActionCardModel model;
  final VoidCallback? onAuthorize;
  final VoidCallback? onCancel;
  final Widget? leading;
  final Widget? authorizeButton;
  final bool showCancel;

  @override
  Widget build(BuildContext context) {
    final light = Theme.of(context).brightness == Brightness.light;
    final foreground = light ? LumenPalette.ink : BrainPalette.textPrimary;
    final muted = light ? LumenPalette.muted : BrainPalette.textMuted;
    return Container(
      key: Key('user_action_${model.moduleId}_${model.taskId}'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: light ? LumenPalette.surfaceMuted : BrainPalette.surfaceSunken,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: light ? LumenPalette.line : BrainPalette.lineStrong,
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: 12)],
              Expanded(
                child: Text(
                  model.displayName ?? model.moduleId,
                  style: BrainType.metaStrong.copyWith(color: foreground),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            model.displayText,
            style: BrainType.body.copyWith(color: foreground),
          ),
          const SizedBox(height: 8),
          Text(
            model.statusText ??
                'Task ${model.taskId} · ${model.continuationState}',
            style: BrainType.meta.copyWith(color: muted),
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 10,
            runSpacing: 8,
            children: [
              authorizeButton ??
                  FilledButton(
                    key: Key('user_action_authorize_${model.moduleId}'),
                    onPressed: onAuthorize,
                    child: Text(model.actionLabel),
                  ),
              if (showCancel)
                OutlinedButton(
                  key: Key('user_action_cancel_${model.moduleId}'),
                  onPressed: onCancel,
                  child: const Text('Cancel'),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
