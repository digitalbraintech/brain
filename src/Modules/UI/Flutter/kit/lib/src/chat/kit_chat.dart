import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../theme/kit_theme.dart';
import 'kit_chat_builders.dart';

/// The shared chat surface for full pages and embedded assistant panels.
///
/// Hosts own conversations, transport, and optional specialized builders. The
/// kit owns the chat presentation and theme in every workspace destination.
final class KitChat extends StatelessWidget {
  const KitChat({
    super.key,
    required this.chatController,
    required this.currentUserId,
    required this.resolveUser,
    this.builders,
    this.onMessageSend,
    this.onAttachmentTap,
  });

  final ChatController chatController;
  final UserID currentUserId;
  final ResolveUserCallback resolveUser;
  final Builders? builders;
  final OnMessageSendCallback? onMessageSend;
  final OnAttachmentTapCallback? onAttachmentTap;

  @override
  Widget build(BuildContext context) => Chat(
    chatController: chatController,
    currentUserId: currentUserId,
    resolveUser: resolveUser,
    builders: KitChatBuilders.withCopy(builders),
    onMessageSend: onMessageSend,
    onAttachmentTap: onAttachmentTap,
    theme: Theme.of(context).brightness == Brightness.light
        ? KitChatTheme.light()
        : KitChatTheme.dark(),
  );
}
