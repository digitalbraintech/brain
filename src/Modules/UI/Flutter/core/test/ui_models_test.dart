import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('unavailable historical activity has no invented timestamp', () {
    final event = BrainActivity.fromJson({
      'id': 'chat:main:Incoming:3',
      'neuronId': 'chat:main',
      'direction': 'Incoming',
      'sequence': 3,
      'signalType': 'Unavailable history',
      'timestamp': null,
      'kind': 'historical',
      'state': 'unavailable',
    });
    expect(event.timestamp, isNull);
    expect(event.kind, 'historical');
    expect(event.sequence, 3);
    expect(event.callerId, isNull);
    expect(event.payloadPreview, isNull);
  });

  test('ChatTurnEvent parses timer offers', () {
    final event = ChatTurnEvent.fromJson({
      'sequence': 7,
      'fromUser': false,
      'text': 'tea in five',
      'commandId': 'c',
      'signal': 'Responded',
      'neuronId': 'n',
      'caller': 'x',
      'correlationId': 'y',
      'timestamp': '2026-08-10T12:00:00Z',
      'timers': [
        {'label': 'tea in five', 'dueAt': '2026-08-10T12:05:00Z'},
      ],
    });

    expect(event.timers, hasLength(1));
    expect(event.timers.first.label, 'tea in five');
    expect(event.timers.first.dueAt, DateTime.utc(2026, 8, 10, 12, 5));
  });

  test('ChatTurnEvent parses kit card refs', () {
    final event = ChatTurnEvent.fromJson({
      'sequence': 8,
      'fromUser': false,
      'text': 'here is the chart',
      'commandId': 'c',
      'signal': 'Responded',
      'neuronId': 'n',
      'caller': 'x',
      'correlationId': 'y',
      'timestamp': '2026-08-10T12:00:00Z',
      'cards': [
        {'kind': 'chart', 'name': 'weekly-usage', 'caption': 'Weekly usage'},
      ],
    });

    expect(event.cards, hasLength(1));
    expect(event.cards.first.kind, 'chart');
    expect(event.cards.first.name, 'weekly-usage');
    expect(event.cards.first.caption, 'Weekly usage');
  });

  test('ChatTurnEvent defaults cards to empty when absent', () {
    final event = ChatTurnEvent.fromJson({
      'sequence': 9,
      'fromUser': false,
      'text': 'no cards here',
      'commandId': 'c',
      'signal': 'Responded',
      'neuronId': 'n',
      'caller': 'x',
      'correlationId': 'y',
      'timestamp': '2026-08-10T12:00:00Z',
    });

    expect(event.cards, isEmpty);
  });

  test('SceneOpenedEvent reads JSON field names used by UI HTTP', () {
    final event = SceneOpenedEvent.fromJson({
      'sequence': 7,
      'sceneKey': 'home',
      'title': 'Home',
      'commandId': 'cmd',
      'shell': 'shell:dev/desk',
    });

    expect(event.sequence, 7);
    expect(event.sceneKey, 'home');
    expect(event.title, 'Home');
    expect(event.commandId, 'cmd');
    expect(event.shell, 'shell:dev/desk');
  });

  test(
    'OpenSceneRequest encodes camelCase for POST /shells/{shell}/scenes',
    () {
      expect(const OpenSceneRequest(sceneKey: 'home', title: 'Home').toJson(), {
        'sceneKey': 'home',
        'title': 'Home',
      });
    },
  );

  test('ActivateControlRequest omits null sceneKey', () {
    expect(const ActivateControlRequest(intent: 'submit').toJson(), {
      'intent': 'submit',
    });
    expect(
      const ActivateControlRequest(intent: 'submit', sceneKey: 'home').toJson(),
      {'intent': 'submit', 'sceneKey': 'home'},
    );
  });

  test('ChatTurnEvent carries durable pulse identity from UI HTTP', () {
    final event = ChatTurnEvent.fromJson({
      'sequence': 9,
      'fromUser': true,
      'text': 'hello',
      'commandId': 'command-9',
      'signal': 'UserMessaged',
      'neuronId': 'chat:owner/main',
      'caller': 'session:owner/session',
      'correlationId': 'correlation-9',
      'timestamp': '2026-07-28T08:00:00Z',
    });

    expect(event.signal, 'UserMessaged');
    expect(event.neuronId, 'chat:owner/main');
    expect(event.caller, 'session:owner/session');
    expect(event.correlationId, 'correlation-9');
    expect(event.timestamp, DateTime.utc(2026, 7, 28, 8));
  });

  test(
    'ChatDelta reads AIContent type-discriminator text frames from the stream',
    () {
      final delta = ChatDelta.fromJson({
        'role': 'assistant',
        'contents': [
          {r'$type': 'text', 'text': 'Hel'},
          {r'$type': 'text', 'text': 'lo'},
        ],
      });

      expect(delta.role, 'assistant');
      expect(delta.contents, hasLength(2));
      expect(delta.contents.every((part) => part.isText), isTrue);
      expect(delta.text, 'Hello');
    },
  );

  test('ChatDeltaPart keeps unknown \$type as opaque raw fields', () {
    final delta = ChatDelta.fromJson({
      'role': 'assistant',
      'contents': [
        {r'$type': 'text', 'text': 'hi'},
        {
          r'$type': 'data',
          'mediaType': 'image/png',
          'uri': 'data:image/png;base64,abc',
        },
      ],
    });

    expect(delta.contents, hasLength(2));
    expect(delta.contents[0].isText, isTrue);
    expect(delta.contents[1].type, 'data');
    expect(delta.contents[1].isText, isFalse);
    expect(delta.contents[1].raw['mediaType'], 'image/png');
    expect(delta.text, 'hi');
  });
}
