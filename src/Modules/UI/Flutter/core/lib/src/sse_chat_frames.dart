import 'dart:convert';

import 'ui_models.dart';

final class SseChatTurnParser {
  String? _dataLine;
  String? _eventName;

  Iterable<ChatTurnEvent> addLine(String line) sync* {
    if (line.startsWith(':')) {
      return;
    }

    if (line.startsWith('event:')) {
      _eventName = line.substring('event:'.length).trim();
      return;
    }

    if (line.startsWith('data:')) {
      _dataLine = line.substring('data:'.length).trim();
      return;
    }

    if (line.isEmpty) {
      yield* _emitBuffered();
    }
  }

  Iterable<ChatTurnEvent> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<ChatTurnEvent> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name != 'chat-turn') {
      return;
    }

    // A malformed known event is a broken contract, not an ignorable event.
    // Surface it instead of silently losing the entire conversation.
    yield ChatTurnEvent.fromJson(jsonDecode(data) as Map<String, dynamic>);
  }
}
