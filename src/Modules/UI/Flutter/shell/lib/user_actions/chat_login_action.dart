import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

enum LoginActionStatus {
  waiting,
  resuming,
  completed,
  cancelling,
  cancelled,
  failed,
  superseded,
  unavailable,
}

const _providerLoginPaths = <String, String>{
  'salesforce': '/integrations/salesforce/login',
  'gmail': '/integrations/gmail/login',
  'github': '/integrations/github/login',
};

/// Projects a single durable action against later events for the same command.
final class ChatLoginAction {
  const ChatLoginAction({
    required this.offer,
    required this.action,
    required this.status,
    this.turnId,
  });

  final ChatTurnEvent offer;
  final ChatUserAction action;
  final LoginActionStatus status;
  final String? turnId;

  String get key => '${offer.commandId}:${action.id}';
  bool get waiting => status == LoginActionStatus.waiting;

  static Map<String, ChatLoginAction> project(List<ChatTurnEvent> turns) {
    final actions = <String, ChatLoginAction>{};
    for (final turn in turns) {
      final offered = turn.userAction;
      for (final entry in actions.entries.toList(growable: false)) {
        final current = entry.value;
        if (current.offer.commandId != turn.commandId) continue;
        final status = switch (turn.signal) {
          'Responded' when offered == null => LoginActionStatus.completed,
          'Responded' when offered?.id != current.action.id =>
            LoginActionStatus.superseded,
          'TurnLifecycle' => switch (turn.status) {
            'WaitingForUser' => LoginActionStatus.waiting,
            'Pending' || 'Running' => LoginActionStatus.resuming,
            'Completed' => LoginActionStatus.completed,
            'Cancelling' => LoginActionStatus.cancelling,
            'Cancelled' => LoginActionStatus.cancelled,
            'Failed' => LoginActionStatus.failed,
            _ => LoginActionStatus.unavailable,
          },
          _ => current.status,
        };
        // A replay must never revive a settled or replaced login link.
        final settled = switch (current.status) {
          LoginActionStatus.completed ||
          LoginActionStatus.cancelled ||
          LoginActionStatus.failed ||
          LoginActionStatus.superseded => true,
          _ => false,
        };
        actions[entry.key] = ChatLoginAction(
          offer: current.offer,
          action: current.action,
          status: settled ? current.status : status,
          turnId: turn.turnId ?? current.turnId,
        );
      }
      if (!turn.fromUser &&
          turn.signal == 'Responded' &&
          offered != null &&
          _providerLoginPaths.containsKey(offered.provider)) {
        final key = '${turn.commandId}:${offered.id}';
        actions.putIfAbsent(
          key,
          () => ChatLoginAction(
            offer: turn,
            action: offered,
            status: LoginActionStatus.waiting,
            turnId: turn.turnId,
          ),
        );
      }
    }
    return actions;
  }
}

/// Auth cards may open only a known provider's login route on the configured
/// kernel. Neither an AI-generated external URL nor a redirect query is accepted.
Uri? trustedLoginUri(ChatUserAction action, Uri? kernelBaseUri) {
  final uri = action.loginUrl;
  if (kernelBaseUri == null ||
      _providerLoginPaths[action.provider] == null ||
      !uri.hasAuthority ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty ||
      uri.hasFragment ||
      kernelBaseUri.userInfo.isNotEmpty ||
      kernelBaseUri.hasFragment ||
      uri.scheme != kernelBaseUri.scheme ||
      uri.host != kernelBaseUri.host ||
      uri.port != kernelBaseUri.port ||
      uri.path != _providerLoginPaths[action.provider]) {
    return null;
  }
  final loopback =
      uri.host == 'localhost' || uri.host == '127.0.0.1' || uri.host == '::1';
  if (!uri.isScheme('https') && !(uri.isScheme('http') && loopback)) {
    return null;
  }
  try {
    final query = uri.queryParametersAll;
    final requests = query['request'];
    if (query.length != 1 ||
        requests == null ||
        requests.length != 1 ||
        requests.single.isEmpty) {
      return null;
    }
  } on FormatException {
    return null;
  }
  return uri;
}

/// Compatibility surface for existing Salesforce callers.
Uri? trustedSalesforceLoginUri(ChatUserAction action, Uri? kernelBaseUri) =>
    trustedLoginUri(action, kernelBaseUri);
