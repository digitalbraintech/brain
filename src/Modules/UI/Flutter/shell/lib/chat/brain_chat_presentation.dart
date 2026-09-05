part of 'brain_chat_screen.dart';

extension _BrainChatPresentation on _BrainChatScreenState {
  Widget _buildPresentation(BuildContext context) => Theme(
    data: KitTheme.light(),
    child: KitThemeScope(
      child: MultiProvider(
        providers: [
          ChangeNotifierProvider.value(value: _streamStates),
          ChangeNotifierProvider.value(value: _voice),
        ],
        child: OverlayPortal(
          controller: _historyPortal,
          overlayChildBuilder: _buildHistoryOverlay,
          child: widget.presentation == BrainChatPresentation.compact
              ? (_historyOpen
                    ? const SizedBox(height: 76)
                    : _buildCompactChat())
              : _buildFullChat(),
        ),
      ),
    ),
  );

  Widget _buildFullChat() => ColoredBox(
    color: LumenPalette.background,
    child: Column(
      children: [
        _voiceNotice(),
        Expanded(
          child: KitChat(
            key: const Key('chat_surface'),
            chatController: _controller,
            currentUserId: ownerUserId,
            resolveUser: (id) async => switch (id) {
              ownerUserId => _BrainChatScreenState._owner,
              assistantUserId => _BrainChatScreenState._assistant,
              _ => null,
            },
            onMessageSend: _canSend ? _handleSend : null,
            onAttachmentTap: widget.onAttachmentTap,
            builders: _chatBuilders(),
          ),
        ),
        _failureNotice(),
      ],
    ),
  );

  bool get _canSend => widget.onSend != null || widget.onStream != null;

  Builders _chatBuilders() => Builders(
    composerBuilder: (context) => BrainChatComposer(
      canVoice: widget.onStreamVoice != null,
      onVoiceTap: () => unawaited(_toggleVoice()),
    ),
    textMessageBuilder:
        (
          context,
          message,
          index, {
          required bool isSentByMe,
          MessageGroupStatus? groupStatus,
        }) => FlyerChatTextMessage(
          message: message,
          index: index,
          showTime: false,
          showStatus: false,
        ),
    textStreamMessageBuilder:
        (
          context,
          message,
          index, {
          required bool isSentByMe,
          MessageGroupStatus? groupStatus,
        }) => FlyerChatTextStreamMessage(
          message: message,
          index: index,
          streamState: context.watch<StreamStateStore>().stateFor(
            message.streamId,
          ),
          showTime: false,
          showStatus: false,
        ),
    customMessageBuilder:
        (
          context,
          message,
          index, {
          required bool isSentByMe,
          MessageGroupStatus? groupStatus,
        }) {
          if (message.metadata?['kind'] == 'user-action') {
            final login = _loginActions[message.metadata?['actionKey']];
            return login == null ? const SizedBox.shrink() : _loginCard(login);
          }
          return KitChatBuilders.customMessageBuilder(
            context,
            message,
            index,
            isSentByMe: isSentByMe,
            groupStatus: groupStatus,
            onReadChart: widget.onReadChart,
            onReadImageBytes: widget.onReadImageBytes,
            onReadSpreadsheet: widget.onReadSpreadsheet,
            onReadGraph: widget.onReadGraph,
          );
        },
  );

  Widget _loginCard(ChatLoginAction login) => switch (login.action.provider) {
    'github' => ProviderLoginCard(
      key: ValueKey(login.key),
      login: login,
      provider: 'github',
      displayName: 'GitHub',
      actionLabel: 'Connect GitHub',
      kernelBaseUri: widget.kernelBaseUri,
      onOpenSignIn: widget.onOpenSignIn,
      onCancelTurn: widget.onCancelTurn,
      leading: const Icon(Icons.code),
    ),
    'salesforce' => SalesforceLoginCard(
      key: ValueKey(login.key),
      login: login,
      kernelBaseUri: widget.kernelBaseUri,
      onOpenSignIn: widget.onOpenSignIn,
      onCancelTurn: widget.onCancelTurn,
    ),
    'gmail' => GmailLoginCard(
      key: ValueKey(login.key),
      login: login,
      kernelBaseUri: widget.kernelBaseUri,
      onOpenSignIn: widget.onOpenSignIn,
      onCancelTurn: widget.onCancelTurn,
    ),
    _ => const SizedBox.shrink(),
  };

  Widget _buildCompactChat() => StreamBuilder<ChatOperation>(
    stream: _controller.operationsStream,
    builder: (context, _) => ListenableBuilder(
      listenable: _streamStates,
      builder: (context, _) {
        final assistantMessages = _controller.messages.where(
          (message) => message.authorId == assistantUserId,
        );
        final textMessages = assistantMessages.where(
          (message) => message is TextMessage || message is TextStreamMessage,
        );
        // Journal recovery appends surviving local requests after durable replies.
        // Their list position is not chronology: an older failed stream must not
        // mask a later answer merely because it never received an acceptance ID.
        Message? latest;
        for (final message in textMessages) {
          if (latest == null ||
              (message.createdAt != null &&
                  (latest.createdAt == null ||
                      !message.createdAt!.isBefore(latest.createdAt!)))) {
            latest = message;
          }
        }
        final (text, waiting) = switch (latest) {
          TextMessage(:final text) => (text, false),
          TextStreamMessage(:final streamId) => switch (_streamStates.stateFor(
            streamId,
          )) {
            StreamStateLoading() => ('Thinking through your request…', true),
            StreamStateStreaming(:final accumulatedText) => (
              accumulatedText,
              true,
            ),
            StreamStateCompleted(:final finalText) => (finalText, false),
            StreamStateError(:final error) => ('$error', false),
          },
          _ => (
            'Ask a question, review your code, or create a behavior.',
            false,
          ),
        };
        final activeLogins = _loginActions.values.where(
          (login) =>
              login.waiting ||
              login.status == LoginActionStatus.resuming ||
              login.status == LoginActionStatus.cancelling,
        );
        final working =
            waiting ||
            _pendingSends.any((pending) {
              final streamId = pending.streamId;
              return streamId != null &&
                  switch (_streamStates.stateFor(streamId)) {
                    StreamStateLoading() || StreamStateStreaming() => true,
                    _ => false,
                  };
            });
        final latestTextIndex = latest == null
            ? -1
            : _controller.messages.indexOf(latest);
        final latestCards = _controller.messages
            .skip(latestTextIndex + 1)
            .whereType<CustomMessage>()
            .where((message) => message.metadata?['kind'] != 'user-action')
            .toList(growable: false);
        return Column(
          key: const Key('compact_chat_surface'),
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DecoratedBox(
              decoration: BoxDecoration(
                color: LumenPalette.surface,
                border: Border.all(color: LumenPalette.line),
                borderRadius: const BorderRadius.vertical(
                  top: Radius.circular(20),
                ),
              ),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(18, 10, 12, 14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        if (working)
                          const SizedBox(
                            width: 12,
                            height: 12,
                            child: CircularProgressIndicator(
                              strokeWidth: 2,
                              color: LumenPalette.accent,
                            ),
                          )
                        else
                          const Icon(
                            Icons.auto_awesome_rounded,
                            size: 16,
                            color: LumenPalette.accent,
                          ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            working ? 'Ino is working on it' : 'Ino',
                            style: const TextStyle(
                              color: LumenPalette.muted,
                              fontSize: 12,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                        LumenActionButton(
                          key: const Key('chat_open_history'),
                          label: 'Full conversation',
                          icon: const Icon(
                            Icons.open_in_full_rounded,
                            size: 13,
                          ),
                          onPressed: _openHistory,
                        ),
                      ],
                    ),
                    const SizedBox(height: 4),
                    ConstrainedBox(
                      constraints: BoxConstraints(
                        maxHeight: widget.compactReplyMaxHeight,
                      ),
                      child: SingleChildScrollView(
                        primary: false,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            KitMarkdown(
                              text,
                              style: const TextStyle(
                                color: LumenPalette.ink,
                                fontSize: 14,
                                height: 1.55,
                              ),
                            ),
                            for (final login in activeLogins) ...[
                              const SizedBox(height: 10),
                              _loginCard(login),
                            ],
                            if (latestCards.isNotEmpty) ...[
                              const SizedBox(height: 10),
                              Align(
                                alignment: Alignment.centerLeft,
                                child: LumenActionButton(
                                  label: latestCards.length == 1
                                      ? 'Open attachment'
                                      : 'Open attachments',
                                  icon: const Icon(
                                    Icons.attach_file_rounded,
                                    size: 14,
                                  ),
                                  onPressed: _openHistory,
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            _voiceNotice(),
            BrainChatComposer(
              embedded: true,
              canVoice: widget.onStreamVoice != null,
              onVoiceTap: () => unawaited(_toggleVoice()),
              onSend: _canSend ? _handleSend : null,
              onAttachmentTap: widget.onAttachmentTap,
            ),
            _failureNotice(),
          ],
        );
      },
    ),
  );

  Widget _buildHistoryOverlay(BuildContext context) => Positioned.fill(
    child: CallbackShortcuts(
      bindings: {
        const SingleActivator(LogicalKeyboardKey.escape): _closeHistory,
      },
      child: Focus(
        autofocus: true,
        child: Stack(
          children: [
            Positioned.fill(
              child: GestureDetector(
                onTap: _closeHistory,
                child: ColoredBox(
                  color: LumenPalette.ink.withValues(alpha: 0.20),
                ),
              ),
            ),
            SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Center(
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(
                      maxWidth: 900,
                      maxHeight: 820,
                    ),
                    child: Material(
                      key: const Key('chat_history_overlay'),
                      color: LumenPalette.background,
                      elevation: 18,
                      shadowColor: LumenPalette.ink.withValues(alpha: 0.15),
                      borderRadius: BorderRadius.circular(24),
                      clipBehavior: Clip.antiAlias,
                      child: Column(
                        children: [
                          Padding(
                            padding: const EdgeInsets.fromLTRB(22, 12, 12, 12),
                            child: Row(
                              children: [
                                const Expanded(
                                  child: Text(
                                    'Conversation',
                                    style: TextStyle(
                                      color: LumenPalette.ink,
                                      fontSize: 18,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                ),
                                LumenIconButton(
                                  key: const Key('chat_close_history'),
                                  icon: const Icon(
                                    Icons.close_rounded,
                                    size: 18,
                                  ),
                                  label: 'Close conversation',
                                  onPressed: _closeHistory,
                                ),
                              ],
                            ),
                          ),
                          const Divider(height: 1, color: LumenPalette.line),
                          Expanded(child: _buildFullChat()),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Widget _voiceNotice() => ListenableBuilder(
    listenable: _voice,
    builder: (context, _) => !_voice.recording && !_voice.busy
        ? const SizedBox.shrink()
        : Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 8),
            child: Text(
              _voice.recording
                  ? 'Recording… tap the microphone to send'
                  : 'Sending voice…',
              style: const TextStyle(color: LumenPalette.accent, fontSize: 12),
            ),
          ),
  );

  Widget _failureNotice() => _failure == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 10),
          child: Text(
            _failure!,
            style: const TextStyle(
              color: LumenPalette.error,
              fontSize: 12,
              height: 1.4,
            ),
          ),
        );
}
