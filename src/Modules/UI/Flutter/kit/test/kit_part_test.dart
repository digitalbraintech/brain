import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('KitButtonPart round-trips metadata for CustomMessage', () {
    const part = KitButtonPart(
      buttonId: 'show-time',
      label: 'Show current time',
      action: 'show-time',
      offerCommandId: 'cmd-1',
    );
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitButtonPart>());
    final button = parsed! as KitButtonPart;
    expect(button.buttonId, 'show-time');
    expect(button.offerCommandId, 'cmd-1');
  });

  test('KitChartPart round-trips points', () {
    const part = KitChartPart(
      title: 'Sales',
      points: [
        KitChartPoint(label: 'Mon', value: 1),
        KitChartPoint(label: 'Tue', value: 2),
      ],
    );
    final parsed = KitPart.tryParse(part.toMetadata()) as KitChartPart;
    expect(parsed.points, hasLength(2));
    expect(parsed.points.first.label, 'Mon');
    expect(parsed.copyText, 'Sales\nMon\t1\nTue\t2');
  });

  test('KitMessageFactory emits CustomMessage for parts', () {
    final messages = KitMessageFactory.messagesForTurn(
      sequence: 2,
      fromUser: false,
      text: 'Tap the button',
      createdAt: DateTime.utc(2026, 8, 9),
      parts: const [
        KitButtonPart(
          buttonId: 'show-time',
          label: 'Show current time',
          action: 'show-time',
        ),
      ],
    );
    expect(messages, hasLength(2));
    expect(messages.first, isA<TextMessage>());
    expect(messages.last, isA<CustomMessage>());
    final custom = messages.last as CustomMessage;
    expect(custom.metadata?['kind'], 'button');
  });

  test('KitChartRefPart round-trips name and caption', () {
    const part = KitChartRefPart(name: 'daily-sales', caption: 'Daily sales');
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitChartRefPart>());
    final ref = parsed! as KitChartRefPart;
    expect(ref.name, 'daily-sales');
    expect(ref.caption, 'Daily sales');
  });

  test('KitSheetPart round-trips cells', () {
    const part = KitSheetPart(
      title: 'Yesterday',
      sheetName: 'Sheet1',
      columns: ['Item', 'Qty'],
      rows: [
        ['Shoes', '2'],
      ],
    );
    final parsed = KitPart.tryParse(part.toMetadata()) as KitSheetPart;
    expect(parsed.title, 'Yesterday');
    expect(parsed.rows.first, ['Shoes', '2']);
  });

  test('KitSheetRefPart round-trips name and caption', () {
    const part = KitSheetRefPart(name: 'sheet-abc', caption: 'Yesterday');
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitSheetRefPart>());
    final ref = parsed! as KitSheetRefPart;
    expect(ref.name, 'sheet-abc');
    expect(ref.caption, 'Yesterday');
  });

  test('KitImageRefPart round-trips name and caption', () {
    const part = KitImageRefPart(name: 'sunset', caption: 'Sunset over bay');
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitImageRefPart>());
    final ref = parsed! as KitImageRefPart;
    expect(ref.name, 'sunset');
    expect(ref.caption, 'Sunset over bay');
  });

  test('KitGraphRefPart round-trips name and caption', () {
    const part = KitGraphRefPart(name: 'graph-abc', caption: 'Module deps');
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitGraphRefPart>());
    final ref = parsed! as KitGraphRefPart;
    expect(ref.name, 'graph-abc');
    expect(ref.caption, 'Module deps');
  });

  test('KitTimerPart round-trips its due instant', () {
    final part = KitTimerPart(
      label: 'tea in five',
      dueAt: DateTime.utc(2026, 8, 10, 12, 30),
    );
    final parsed = KitPart.tryParse(part.toMetadata());
    expect(parsed, isA<KitTimerPart>());
    final timer = parsed! as KitTimerPart;
    expect(timer.label, 'tea in five');
    expect(timer.dueAt, DateTime.utc(2026, 8, 10, 12, 30));
  });
}
