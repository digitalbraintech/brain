import 'dart:convert';

import 'ui_models.dart';

final class SseChatDeltaParser {
  String? _dataLine;
  String? _eventName;

  Iterable<ChatDelta> addLine(String line) sync* {
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

  Iterable<ChatDelta> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<ChatDelta> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name == 'chat-accepted') {
      final payload = jsonDecode(data) as Map<String, dynamic>;
      yield ChatDelta.accepted(
        commandId: payload['commandId'] as String,
        turnId: payload['turnId'] as String,
      );
      return;
    }
    if (name == 'chat-error') {
      final payload = jsonDecode(data) as Map<String, dynamic>;
      throw StateError(
        payload['message'] as String? ??
            'The assistant could not complete this turn.',
      );
    }
    if (name != 'chat-delta') {
      return;
    }

    yield ChatDelta.fromJson(jsonDecode(data) as Map<String, dynamic>);
  }
}
