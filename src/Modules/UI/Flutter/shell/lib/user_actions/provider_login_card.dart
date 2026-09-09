import 'dart:async';

import 'package:flutter/material.dart';

import '../chat/chat_contracts.dart';
import 'chat_login_action.dart';
import 'user_action_card.dart';

typedef LoginAuthorizeButtonBuilder = Widget Function(
  BuildContext context,
  VoidCallback? onPressed,
);

/// Shared lifecycle card for the small, explicitly allowlisted set of OAuth
/// providers that can be requested by a chat turn.
final class ProviderLoginCard extends StatefulWidget {
  const ProviderLoginCard({
    super.key,
    required this.login,
    required this.provider,
    required this.displayName,
    required this.actionLabel,
    this.kernelBaseUri,
    this.onOpenSignIn,
    this.onCancelTurn,
    this.leading,
    this.authorizeButtonBuilder,
  });

  final ChatLoginAction login;
  final String provider;
  final String displayName;
  final String actionLabel;
  final Uri? kernelBaseUri;
  final OpenUrl? onOpenSignIn;
  final CancelChatTurn? onCancelTurn;
  final Widget? leading;
  final LoginAuthorizeButtonBuilder? authorizeButtonBuilder;

  @override
  State<ProviderLoginCard> createState() => _ProviderLoginCardState();
}

final class _ProviderLoginCardState extends State<ProviderLoginCard> {
  Timer? _expiryTimer;
  bool _opening = false;
  bool _opened = false;
  bool _cancelling = false;
  bool _cancelRequested = false;
  String? _failure;

  bool get _expired =>
      !DateTime.now().toUtc().isBefore(widget.login.action.expiresAt);

  @override
  void initState() {
    super.initState();
    _scheduleExpiry();
  }

  @override
  void didUpdateWidget(covariant ProviderLoginCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.login.action.expiresAt != widget.login.action.expiresAt ||
        oldWidget.login.status != widget.login.status) {
      _scheduleExpiry();
    }
  }

  void _scheduleExpiry() {
    _expiryTimer?.cancel();
    if (!widget.login.waiting || _expired) return;
    _expiryTimer = Timer(
      widget.login.action.expiresAt.difference(DateTime.now().toUtc()),
      () {
        if (mounted) setState(() {});
      },
    );
  }

  Future<void> _open() async {
    final uri = trustedLoginUri(widget.login.action, widget.kernelBaseUri);
    final open = widget.onOpenSignIn;
    if (uri == null ||
        open == null ||
        !widget.login.waiting ||
        widget.login.action.stage != 'login' ||
        _expired ||
        _opening ||
        _cancelling ||
        _cancelRequested) {
      return;
    }
    setState(() {
      _opening = true;
      _failure = null;
    });
    try {
      // Keep this directly in the click handler so browsers allow the popup.
      await open(uri);
      if (mounted) setState(() => _opened = true);
    } on Object {
      if (mounted) {
        setState(() => _failure = 'Could not open your browser. Try again.');
      }
    } finally {
      if (mounted) setState(() => _opening = false);
    }
  }

  Future<void> _cancel() async {
    final cancel = widget.onCancelTurn;
    final turnId = widget.login.turnId;
    if (cancel == null ||
        turnId == null ||
        turnId.isEmpty ||
        !widget.login.waiting ||
        _cancelling ||
        _cancelRequested) {
      return;
    }
    setState(() {
      _cancelling = true;
      _failure = null;
    });
    try {
      await cancel(commandId: widget.login.offer.commandId, turnId: turnId);
      if (mounted) setState(() => _cancelRequested = true);
    } on Object {
      if (mounted) {
        setState(() => _failure = 'Could not cancel the request. Try again.');
      }
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  String _statusText(bool trusted) {
    if (!widget.login.waiting) {
      return switch (widget.login.status) {
        LoginActionStatus.resuming => 'Continuing your request…',
        LoginActionStatus.completed => 'Request completed.',
        LoginActionStatus.cancelling => 'Cancelling request…',
        LoginActionStatus.cancelled => 'Request cancelled.',
        LoginActionStatus.failed => 'Request failed. Send your request again.',
        LoginActionStatus.superseded =>
          'A newer sign-in request replaced this one.',
        _ => 'This sign-in request is no longer available.',
      };
    }
    if (_cancelling || _cancelRequested) {
      return 'Cancelling request…';
    }
    if (_expired) {
      if (widget.login.action.stage == 'readiness') {
        return 'Verification timed out. Your saved behavior draft is retained.';
      }
      return 'Sign-in expired. Send your request again to reconnect.';
    }
    if (widget.login.action.stage == 'readiness') {
      return 'Waiting for verified webhook delivery. You can close the browser.';
    }
    if (!trusted) {
      return 'This sign-in link could not be verified.';
    }
    if (_opening) {
      return 'Opening your browser…';
    }
    if (_opened) {
      return 'Finish signing in in your browser. Your request will continue here.';
    }
    return 'Sign in securely in your browser to continue this request.';
  }

  @override
  Widget build(BuildContext context) {
    final login = widget.login;
    final trusted =
        login.action.provider == widget.provider &&
        trustedLoginUri(login.action, widget.kernelBaseUri) != null;
    final active = login.waiting && !_cancelling && !_cancelRequested;
    final canAuthorize =
        active &&
        login.action.stage == 'login' &&
        !_expired &&
        !_opening &&
        trusted &&
        widget.onOpenSignIn != null;
    final authorize = canAuthorize ? () => unawaited(_open()) : null;
    return ConstrainedBox(
      constraints: const BoxConstraints(maxWidth: 440),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UserActionCard(
            model: UserActionCardModel(
              moduleId: widget.provider,
              displayName: widget.displayName,
              displayText: login.action.message,
              actionUrl: login.action.loginUrl,
              taskId: login.offer.commandId,
              actionLabel: widget.actionLabel,
              statusText: _statusText(trusted),
            ),
            leading: widget.leading,
            authorizeButton: widget.authorizeButtonBuilder?.call(
              context,
              authorize,
            ),
            onAuthorize: authorize,
            showCancel: true,
            onCancel:
                active &&
                    !_opening &&
                    login.turnId != null &&
                    login.turnId!.isNotEmpty &&
                    widget.onCancelTurn != null
                ? () => unawaited(_cancel())
                : null,
          ),
          if (_failure != null && login.waiting)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Text(
                _failure!,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: Theme.of(context).colorScheme.error),
              ),
            ),
        ],
      ),
    );
  }

  @override
  void dispose() {
    _expiryTimer?.cancel();
    super.dispose();
  }
}
