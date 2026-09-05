import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/user_actions/chat_login_action.dart';
import 'package:digitalbrain_flutter_shell/user_actions/provider_login_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'verified login waiting for webhook keeps cancel available and disables reauthorization',
    (tester) async {
      final action = ChatUserAction.tryParse({
        'id': 'action',
        'provider': 'github',
        'displayName': 'GitHub',
        'message': 'GitHub connected. Waiting for a signed webhook.',
        'loginUrl':
            'http://localhost:5080/integrations/github/login?request=${'a' * 64}',
        'expiresAt': DateTime.now()
            .toUtc()
            .add(const Duration(minutes: 10))
            .toIso8601String(),
        'stage': 'readiness',
      })!;
      final offer = ChatTurnEvent(
        sequence: 1,
        fromUser: false,
        text: action.message,
        commandId: 'command',
        signal: 'Responded',
        neuronId: 'chat:main',
        caller: 'assistant',
        correlationId: 'correlation',
        timestamp: DateTime.now(),
        userAction: action,
        turnId: 'turn',
      );
      var opened = false;
      var cancelled = false;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ProviderLoginCard(
              login: ChatLoginAction(
                offer: offer,
                action: action,
                status: LoginActionStatus.waiting,
                turnId: 'turn',
              ),
              provider: 'github',
              displayName: 'GitHub',
              actionLabel: 'Connect GitHub',
              kernelBaseUri: Uri.parse('http://localhost:5080'),
              onOpenSignIn: (_) async {
                opened = true;
              },
              onCancelTurn: ({required commandId, required turnId}) async {
                cancelled = true;
              },
            ),
          ),
        ),
      );
      expect(
        find.textContaining('Waiting for verified webhook'),
        findsOneWidget,
      );
      await tester.tap(find.text('Connect GitHub'));
      expect(opened, isFalse);
      await tester.tap(find.text('Cancel'));
      await tester.pump();
      expect(cancelled, isTrue);
      await tester.pumpWidget(const SizedBox());
    },
  );
}
