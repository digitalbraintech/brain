import 'dart:convert';

import 'ui_models.dart';

final class SseSceneOpenedParser {
  String? _dataLine;
  String? _eventName;

  Iterable<SceneOpenedEvent> addLine(String line) sync* {
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

  Iterable<SceneOpenedEvent> flush() sync* {
    yield* _emitBuffered();
  }

  Iterable<SceneOpenedEvent> _emitBuffered() sync* {
    final data = _dataLine;
    final name = _eventName;
    _dataLine = null;
    _eventName = null;

    if (data == null) {
      return;
    }
    if (name != 'surface-opened' && name != 'scene-opened') {
      return;
    }

    final event = _decode(data);
    if (event != null) {
      yield event;
    }
  }

  static SceneOpenedEvent? _decode(String payload) {
    try {
      final decoded = jsonDecode(payload);
      if (decoded is! Map) {
        return null;
      }
      return SceneOpenedEvent.fromJson(Map<String, Object?>.from(decoded));
    } on FormatException {
      return null;
    } on TypeError {
      return null;
    }
  }
}
