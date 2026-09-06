import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';

typedef SendMessage = Future<void> Function(String text);
typedef StreamMessage = Stream<ChatDelta> Function(String text);
typedef StreamVoice =
    Stream<ChatDelta> Function(List<int> audioBytes, {String fileName});
typedef OpenUrl = Future<void> Function(Uri url);
typedef CancelChatTurn =
    Future<void> Function({required String commandId, required String turnId});
typedef ReadChart = Future<ChatChartOffer?> Function(String name);
typedef ReadImageBytes = Future<Uint8List?> Function(String name);
typedef ReadSpreadsheet = Future<ChatSpreadsheetOffer?> Function(String name);
typedef ReadGraph = Future<ChatGraphOffer?> Function(String name);
typedef ReadSurface = Future<KitSurfaceState?> Function(String name);

const ownerUserId = 'owner';
const assistantUserId = 'assistant';
const onboardingDestinationIndex = 1;
const graphDestinationIndex = 2;
const activityDestinationIndex = 3;
const kitDestinationIndex = 4;
const windowingDestinationIndex = 5;

extension ChatTurnKitParts on ChatTurnEvent {
  List<KitPart> get kitParts => [
    for (final button in buttons)
      KitButtonPart(
        buttonId: button.buttonId,
        label: button.label,
        action: button.action,
        offerCommandId: commandId,
      ),
    for (final chart in charts)
      KitChartPart(
        title: chart.title,
        points: [
          for (final point in chart.points)
            KitChartPoint(label: point.label, value: point.value),
        ],
        chartKind: chart.chartKind,
      ),
    for (final timer in timers)
      KitTimerPart(label: timer.label, dueAt: timer.dueAt),
    for (final card in cards)
      ...switch (card.kind) {
        'chart' => [KitChartRefPart(name: card.name, caption: card.caption)],
        'image' => [KitImageRefPart(name: card.name, caption: card.caption)],
        'spreadsheet' => [
          KitSheetRefPart(name: card.name, caption: card.caption),
        ],
        'graph' => [KitGraphRefPart(name: card.name, caption: card.caption)],
        _ => const <KitPart>[],
      },
  ];
}
