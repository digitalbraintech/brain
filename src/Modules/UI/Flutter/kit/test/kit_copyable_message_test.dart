import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('chart copyText is title plus TSV points', () {
    const part = KitChartPart(
      title: 'Sales',
      points: [
        KitChartPoint(label: 'Mon', value: 1),
        KitChartPoint(label: 'Tue', value: 2),
      ],
    );
    expect(part.copyText, 'Sales\nMon\t1\nTue\t2');
  });

  test('card copyText joins title, body, and fields', () {
    const part = KitCardPart(
      title: 'PR',
      body: 'Looks good',
      fields: [(label: 'Repo', value: 'digitalbrain')],
    );
    expect(part.copyText, 'PR\nLooks good\nRepo: digitalbrain');
  });

  testWidgets('copy control writes the message text to the clipboard', (
    tester,
  ) async {
    String? copied;
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async {
        if (call.method == 'Clipboard.setData') {
          copied = (call.arguments as Map)['text'] as String?;
        }
        return null;
      },
    );

    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: KitCopyableMessage(
            copyText: _fixedCopy,
            child: Text('Hello from Ino'),
          ),
        ),
      ),
    );

    expect(find.byType(SelectionArea), findsOneWidget);
    await tester.tap(find.byKey(const Key('kit_copy_message')));
    await tester.pump();
    expect(copied, 'Hello from Ino');
    await tester.pump(const Duration(milliseconds: 1400));
  });

  testWidgets('KitChat wraps text bubbles with a copy control', (tester) async {
    final controller = InMemoryChatController(
      messages: [
        TextMessage(
          id: 'm1',
          authorId: 'assistant',
          createdAt: DateTime.utc(2026, 9, 6),
          text: 'synapse demo ok',
        ),
      ],
    );

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SizedBox(
            height: 400,
            child: KitChat(
              chatController: controller,
              currentUserId: 'owner',
              resolveUser: (id) async => User(id: id),
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));

    expect(find.byType(KitCopyableMessage), findsOneWidget);
    expect(find.byKey(const Key('kit_copy_message')), findsOneWidget);
    expect(find.byType(SelectionArea), findsWidgets);
  });
}

String _fixedCopy(BuildContext context) => 'Hello from Ino';
