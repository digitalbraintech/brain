final class SendMessageRequest {
  const SendMessageRequest({required this.text});

  final String text;

  Map<String, Object?> toJson() => {'text': text};
}

/// An acceptance receipt or assistant content from the chat command stream.
///
/// Unknown content `$type` values are retained as [ChatDeltaPart] with raw
/// fields so older clients do not crash when the edge starts emitting data/uri.

final class ChatDelta {
  const ChatDelta({required this.role, required this.contents})
    : commandId = null,
      turnId = null;

  const ChatDelta.accepted({required this.commandId, required this.turnId})
    : role = null,
      contents = const [];

  final String? role;
  final List<ChatDeltaPart> contents;
  final String? commandId;
  final String? turnId;
  bool get isAcceptance => commandId != null;

  String get text => contents
      .map((part) => part.text ?? '')
      .where((value) => value.isNotEmpty)
      .join();

  factory ChatDelta.fromJson(Map<String, Object?> json) {
    final rawContents = json['contents'];
    final contents = rawContents is List
        ? rawContents
              .whereType<Map>()
              .map(
                (part) =>
                    ChatDeltaPart.fromJson(Map<String, Object?>.from(part)),
              )
              .toList(growable: false)
        : const <ChatDeltaPart>[];

    return ChatDelta(role: json['role'] as String?, contents: contents);
  }
}

final class ChatDeltaPart {
  const ChatDeltaPart({required this.type, this.text, required this.raw});

  final String type;
  final String? text;
  final Map<String, Object?> raw;

  bool get isText => type == 'text';

  factory ChatDeltaPart.fromJson(Map<String, Object?> json) {
    final type = json[r'$type'] as String? ?? 'unknown';
    return ChatDeltaPart(
      type: type,
      text: json['text'] as String?,
      raw: Map<String, Object?>.from(json),
    );
  }
}

final class ChatButtonOffer {
  const ChatButtonOffer({
    required this.buttonId,
    required this.label,
    required this.action,
  });

  final String buttonId;
  final String label;
  final String action;

  factory ChatButtonOffer.fromJson(Map<String, Object?> json) {
    return ChatButtonOffer(
      buttonId: json['buttonId'] as String? ?? '',
      label: json['label'] as String? ?? '',
      action: json['action'] as String? ?? '',
    );
  }
}

final class ChatChartPoint {
  const ChatChartPoint({required this.label, required this.value});

  final String label;
  final num value;

  factory ChatChartPoint.fromJson(Map<String, Object?> json) {
    return ChatChartPoint(
      label: json['label'] as String? ?? '',
      value: json['value'] as num? ?? 0,
    );
  }
}

final class ChatSpreadsheetOffer {
  const ChatSpreadsheetOffer({
    required this.title,
    required this.columns,
    required this.rows,
    this.sheetName = 'Sheet1',
  });

  final String title;
  final String sheetName;
  final List<String> columns;
  final List<List<String>> rows;

  factory ChatSpreadsheetOffer.fromJson(Map<String, Object?> json) {
    final rawColumns = json['columns'];
    final columns = rawColumns is List
        ? rawColumns.map((e) => e.toString()).toList(growable: false)
        : const <String>[];
    final rawRows = json['rows'];
    final rows = rawRows is List
        ? rawRows
              .map((row) {
                if (row is List) {
                  return row.map((cell) => cell.toString()).toList();
                }
                if (row is Map) {
                  final cells = row['cells'];
                  if (cells is List) {
                    return cells.map((cell) => cell.toString()).toList();
                  }
                }
                return const <String>[];
              })
              .toList(growable: false)
        : const <List<String>>[];
    return ChatSpreadsheetOffer(
      title: json['title'] as String? ?? 'Sheet',
      sheetName: json['sheetName'] as String? ?? 'Sheet1',
      columns: columns,
      rows: rows,
    );
  }
}

final class ChatGraphNode {
  const ChatGraphNode({
    required this.id,
    required this.label,
    this.kind = 'leaf',
    this.cluster,
  });

  final String id;
  final String label;
  final String kind;
  final String? cluster;

  factory ChatGraphNode.fromJson(Map<String, Object?> json) => ChatGraphNode(
    id: json['id'] as String? ?? '',
    label: json['label'] as String? ?? '',
    kind: json['kind'] as String? ?? 'leaf',
    cluster: json['cluster'] as String?,
  );
}

final class ChatGraphEdge {
  const ChatGraphEdge({
    required this.id,
    required this.sourceId,
    required this.targetId,
    this.dotted = false,
  });

  final String id;
  final String sourceId;
  final String targetId;
  final bool dotted;

  factory ChatGraphEdge.fromJson(Map<String, Object?> json) => ChatGraphEdge(
    id: json['id'] as String? ?? '',
    sourceId: json['sourceId'] as String? ?? '',
    targetId: json['targetId'] as String? ?? '',
    dotted: json['dotted'] as bool? ?? false,
  );
}

/// State of a named graph kit entity, read from /kit/graphs/{name}.
final class ChatGraphOffer {
  const ChatGraphOffer({
    required this.title,
    required this.nodes,
    required this.edges,
  });

  final String title;
  final List<ChatGraphNode> nodes;
  final List<ChatGraphEdge> edges;

  factory ChatGraphOffer.fromJson(Map<String, Object?> json) {
    final rawNodes = json['nodes'];
    final rawEdges = json['edges'];
    return ChatGraphOffer(
      title: json['title'] as String? ?? '',
      nodes: rawNodes is List
          ? rawNodes
                .whereType<Map>()
                .map(
                  (e) => ChatGraphNode.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <ChatGraphNode>[],
      edges: rawEdges is List
          ? rawEdges
                .whereType<Map>()
                .map(
                  (e) => ChatGraphEdge.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <ChatGraphEdge>[],
    );
  }
}

final class ChatChartOffer {
  const ChatChartOffer({
    required this.title,
    required this.points,
    this.chartKind = 'bar',
  });

  final String title;
  final List<ChatChartPoint> points;
  final String chartKind;

  factory ChatChartOffer.fromJson(Map<String, Object?> json) {
    final raw = json['points'];
    final points = raw is List
        ? raw
              .whereType<Map>()
              .map((e) => ChatChartPoint.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <ChatChartPoint>[];
    return ChatChartOffer(
      title: json['title'] as String? ?? 'Chart',
      points: points,
      chartKind: json['chartKind'] as String? ?? 'bar',
    );
  }
}

final class KitCardRef {
  const KitCardRef({
    required this.kind,
    required this.name,
    required this.caption,
  });

  final String kind;
  final String name;
  final String caption;

  factory KitCardRef.fromJson(Map<String, Object?> json) => KitCardRef(
    kind: json['kind'] as String? ?? '',
    name: json['name'] as String? ?? '',
    caption: json['caption'] as String? ?? '',
  );
}

/// Non-secret information needed to continue a turn in the system browser.
final class ChatUserAction {
  const ChatUserAction({
    required this.id,
    required this.provider,
    required this.displayName,
    required this.message,
    required this.loginUrl,
    required this.expiresAt,
    this.resumeToolNames = const [],
    this.stage = 'login',
  });

  final String id;
  final String provider;
  final String displayName;
  final String message;
  final Uri loginUrl;
  final DateTime expiresAt;
  final List<String> resumeToolNames;
  final String stage;

  static ChatUserAction? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = value['id'];
    final provider = value['provider'];
    final displayName = value['displayName'];
    final message = value['message'];
    final loginUrl = value['loginUrl'];
    final expiresAt = value['expiresAt'];
    if (id is! String ||
        id.isEmpty ||
        provider is! String ||
        displayName is! String ||
        message is! String ||
        loginUrl is! String ||
        expiresAt is! String) {
      return null;
    }
    final uri = Uri.tryParse(loginUrl);
    final expiry = DateTime.tryParse(expiresAt);
    if (uri == null || expiry == null) return null;
    final tools = value['resumeToolNames'];
    return ChatUserAction(
      id: id,
      provider: provider,
      displayName: displayName,
      message: message,
      loginUrl: uri,
      expiresAt: expiry.toUtc(),
      stage: value['stage'] as String? ?? 'login',
      resumeToolNames: tools is List
          ? List<String>.unmodifiable(tools.whereType<String>())
          : const [],
    );
  }
}

final class ChatTurnEvent {
  const ChatTurnEvent({
    required this.sequence,
    required this.fromUser,
    required this.text,
    required this.commandId,
    required this.signal,
    required this.neuronId,
    required this.caller,
    required this.correlationId,
    required this.timestamp,
    this.buttons = const [],
    this.charts = const [],
    this.timers = const [],
    this.cards = const [],
    this.turnId,
    this.status,
    this.userAction,
  });

  final int sequence;
  final bool fromUser;
  final String text;
  final String commandId;
  final String signal;
  final String neuronId;
  final String caller;
  final String correlationId;
  final DateTime timestamp;
  final List<ChatButtonOffer> buttons;
  final List<ChatChartOffer> charts;
  final List<ChatTimerOffer> timers;
  final List<KitCardRef> cards;
  final String? turnId;
  final String? status;
  final ChatUserAction? userAction;

  factory ChatTurnEvent.fromJson(Map<String, Object?> json) {
    final rawButtons = json['buttons'];
    final buttons = rawButtons is List
        ? rawButtons
              .whereType<Map>()
              .map(
                (e) => ChatButtonOffer.fromJson(Map<String, Object?>.from(e)),
              )
              .toList(growable: false)
        : const <ChatButtonOffer>[];
    final rawCharts = json['charts'];
    final charts = rawCharts is List
        ? rawCharts
              .whereType<Map>()
              .map((e) => ChatChartOffer.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <ChatChartOffer>[];
    final rawTimers = json['timers'];
    final timers = rawTimers is List
        ? rawTimers
              .whereType<Map>()
              .map((e) => ChatTimerOffer.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <ChatTimerOffer>[];
    final rawCards = json['cards'];
    final cards = rawCards is List
        ? rawCards
              .whereType<Map>()
              .map((e) => KitCardRef.fromJson(Map<String, Object?>.from(e)))
              .toList(growable: false)
        : const <KitCardRef>[];

    return ChatTurnEvent(
      sequence: (json['sequence'] as num).toInt(),
      fromUser: json['fromUser'] as bool,
      text: json['text'] as String,
      commandId: json['commandId'] as String,
      signal: json['signal'] as String,
      neuronId: json['neuronId'] as String,
      caller: json['caller'] as String,
      correlationId: json['correlationId'] as String,
      timestamp: DateTime.parse(json['timestamp'] as String).toUtc(),
      buttons: buttons,
      charts: charts,
      timers: timers,
      cards: cards,
      turnId: json['turnId'] as String?,
      status: json['status'] as String?,
      userAction: ChatUserAction.tryParse(json['userAction']),
    );
  }
}

final class ChatTimerOffer {
  const ChatTimerOffer({required this.label, required this.dueAt});

  final String label;
  final DateTime dueAt;

  factory ChatTimerOffer.fromJson(Map<String, Object?> json) {
    final rawDueAt = json['dueAt'] as String?;
    return ChatTimerOffer(
      label: json['label'] as String? ?? 'Timer',
      dueAt: rawDueAt == null
          ? DateTime.now().toUtc()
          : DateTime.parse(rawDueAt).toUtc(),
    );
  }
}

/// State of a named surface kit entity, read from /kit/surfaces/{name}.
final class KitSurfaceState {
  const KitSurfaceState({required this.scenes});

  final List<KitSurfaceScene> scenes;

  factory KitSurfaceState.fromJson(Map<String, Object?> json) {
    final raw = json['scenes'];
    return KitSurfaceState(
      scenes: raw is List
          ? raw
                .whereType<Map>()
                .map(
                  (e) => KitSurfaceScene.fromJson(Map<String, Object?>.from(e)),
                )
                .toList(growable: false)
          : const <KitSurfaceScene>[],
    );
  }
}

final class KitSurfaceScene {
  const KitSurfaceScene({required this.surfaceKey, required this.title});

  final String surfaceKey;
  final String title;

  factory KitSurfaceScene.fromJson(Map<String, Object?> json) => KitSurfaceScene(
    surfaceKey: json['surfaceKey'] as String? ?? '',
    title: json['title'] as String? ?? '',
  );
}
