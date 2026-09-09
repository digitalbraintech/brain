import 'dart:typed_data';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../components/button/kit_button.dart';
import '../components/card/kit_card.dart';
import '../components/chart/kit_chart.dart';
import '../components/graph/graph_models.dart';
import '../components/graph/graph_scene.dart';
import '../components/graph/kit_graph_controller.dart';
import '../components/graph/kit_graph_navigator.dart';
import '../components/graph/kit_graph_view.dart';
import '../components/clock/kit_clock.dart';
import '../components/image/kit_image.dart';
import '../components/sheet/kit_sheet.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';
import 'kit_copyable_message.dart';

typedef KitButtonPressed = void Function(KitButtonPart part);
typedef KitChartRefReader = Future<ChatChartOffer?> Function(String name);
typedef KitImageRefReader = Future<Uint8List?> Function(String name);
typedef KitSheetRefReader = Future<ChatSpreadsheetOffer?> Function(String name);
typedef KitGraphRefReader = Future<ChatGraphOffer?> Function(String name);

/// Flyer Chat [Builders] helpers for DigitalBrain kit components.
///
/// Official extension point: `Builders.customMessageBuilder` receives
/// [CustomMessage]; payload lives in `message.metadata` (kind-discriminated).
/// Docs: https://pub.dev/packages/flutter_chat_ui
abstract final class KitChatBuilders {
  static Widget customMessageBuilder(
    BuildContext context,
    CustomMessage message,
    int index, {
    required bool isSentByMe,
    MessageGroupStatus? groupStatus,
    KitButtonPressed? onButtonPressed,
    KitChartRefReader? onReadChart,
    KitImageRefReader? onReadImageBytes,
    KitSheetRefReader? onReadSpreadsheet,
    KitGraphRefReader? onReadGraph,
    GraphSceneFactory? graphSceneFactory,
  }) {
    final part = KitPart.tryParse(
      message.metadata == null
          ? null
          : Map<String, dynamic>.from(message.metadata!),
    );

    if (part == null) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 4),
        child: Text(
          'Unsupported kit message',
          style: KitType.bodyMuted,
          key: Key('kit_custom_unsupported'),
        ),
      );
    }

    return KitCopyableMessage(
      copyText: (_) => part.copyText,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: switch (part) {
          KitButtonPart(:final buttonId) => KitButton(
            key: Key('chat_kit_button_$buttonId'),
            part: part,
            dense: true,
            onPressed: onButtonPressed,
          ),
          KitChartPart() => KitChart(part: part, height: 180),
          KitCardPart() => KitCard(part: part),
          KitTimerPart() => KitClock(part: part),
          KitChartRefPart(:final name, :final caption) => _KitChartRefLoader(
            name: name,
            caption: caption,
            reader: onReadChart,
          ),
          KitImageRefPart(:final name, :final caption) => _KitImageRefLoader(
            name: name,
            caption: caption,
            reader: onReadImageBytes,
          ),
          KitSheetPart() => KitSheet(part: part),
          KitSheetRefPart(:final name, :final caption) => _KitSheetRefLoader(
            name: name,
            caption: caption,
            reader: onReadSpreadsheet,
          ),
          KitGraphRefPart(:final name, :final caption) => _KitGraphRefLoader(
            name: name,
            caption: caption,
            reader: onReadGraph,
            sceneFactory: graphSceneFactory,
          ),
        },
      ),
    );
  }

  /// Wraps text, stream, and custom bubbles with selection + copy.
  static Builders withCopy(Builders? host) {
    final base = host ?? const Builders();
    final text = base.textMessageBuilder;
    final stream = base.textStreamMessageBuilder;
    final custom = base.customMessageBuilder;
    var builders = base.copyWith(
      textMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            final child =
                text?.call(
                  context,
                  message,
                  index,
                  isSentByMe: isSentByMe,
                  groupStatus: groupStatus,
                ) ??
                SimpleTextMessage(message: message, index: index);
            return KitCopyableMessage(
              copyText: (_) => message.text,
              child: child,
            );
          },
    );
    if (stream != null) {
      builders = builders.copyWith(
        textStreamMessageBuilder:
            (
              context,
              message,
              index, {
              required bool isSentByMe,
              MessageGroupStatus? groupStatus,
            }) {
              return KitCopyableMessage(
                copyText: (context) =>
                    KitStreamCopy.streamText(context, message.streamId),
                child: stream(
                  context,
                  message,
                  index,
                  isSentByMe: isSentByMe,
                  groupStatus: groupStatus,
                ),
              );
            },
      );
    }
    if (custom != null) {
      builders = builders.copyWith(
        customMessageBuilder:
            (
              context,
              message,
              index, {
              required bool isSentByMe,
              MessageGroupStatus? groupStatus,
            }) {
              final child = custom(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              );
              if (child is KitCopyableMessage) {
                return child;
              }
              final part = KitPart.tryParse(
                message.metadata == null
                    ? null
                    : Map<String, dynamic>.from(message.metadata!),
              );
              final copy = part?.copyText ?? '';
              if (copy.trim().isEmpty) {
                return child;
              }
              return KitCopyableMessage(copyText: (_) => copy, child: child);
            },
      );
    }
    return builders;
  }

  /// Drop-in partial [Builders] for chat surfaces that only need kit customs.
  static Builders kitCustoms({
    KitButtonPressed? onButtonPressed,
    KitChartRefReader? onReadChart,
    KitImageRefReader? onReadImageBytes,
    KitSheetRefReader? onReadSpreadsheet,
    KitGraphRefReader? onReadGraph,
  }) {
    return Builders(
      customMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) => customMessageBuilder(
            context,
            message,
            index,
            isSentByMe: isSentByMe,
            groupStatus: groupStatus,
            onButtonPressed: onButtonPressed,
            onReadChart: onReadChart,
            onReadImageBytes: onReadImageBytes,
            onReadSpreadsheet: onReadSpreadsheet,
            onReadGraph: onReadGraph,
          ),
    );
  }
}

final class _KitChartRefLoader extends StatefulWidget {
  const _KitChartRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final KitChartRefReader? reader;

  @override
  State<_KitChartRefLoader> createState() => _KitChartRefLoaderState();
}

final class _KitChartRefLoaderState extends State<_KitChartRefLoader> {
  Future<ChatChartOffer?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('kit_chart_ref_offline_${widget.name}'),
        style: KitType.bodyMuted,
      );
    }

    return FutureBuilder<ChatChartOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('kit_chart_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('kit_chart_ref_missing_${widget.name}'),
            style: KitType.bodyMuted,
          );
        }

        return KitChart(
          part: KitChartPart(
            title: offer.title,
            points: [
              for (final point in offer.points)
                KitChartPoint(label: point.label, value: point.value),
            ],
            chartKind: offer.chartKind,
          ),
          height: 180,
        );
      },
    );
  }
}

final class _KitImageRefLoader extends StatefulWidget {
  const _KitImageRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final KitImageRefReader? reader;

  @override
  State<_KitImageRefLoader> createState() => _KitImageRefLoaderState();
}

final class _KitImageRefLoaderState extends State<_KitImageRefLoader> {
  Future<Uint8List?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('kit_image_ref_offline_${widget.name}'),
        style: KitType.bodyMuted,
      );
    }

    return FutureBuilder<Uint8List?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('kit_image_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final bytes = snapshot.data;
        if (bytes == null) {
          return Text(
            widget.caption,
            key: Key('kit_image_ref_missing_${widget.name}'),
            style: KitType.bodyMuted,
          );
        }

        return KitImage(bytes: bytes, caption: widget.caption);
      },
    );
  }
}

final class _KitSheetRefLoader extends StatefulWidget {
  const _KitSheetRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
  });

  final String name;
  final String caption;
  final KitSheetRefReader? reader;

  @override
  State<_KitSheetRefLoader> createState() => _KitSheetRefLoaderState();
}

final class _KitSheetRefLoaderState extends State<_KitSheetRefLoader> {
  Future<ChatSpreadsheetOffer?>? _fetch;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('kit_sheet_ref_offline_${widget.name}'),
        style: KitType.bodyMuted,
      );
    }

    return FutureBuilder<ChatSpreadsheetOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('kit_sheet_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('kit_sheet_ref_missing_${widget.name}'),
            style: KitType.bodyMuted,
          );
        }

        return KitSheet(
          part: KitSheetPart(
            title: offer.title,
            sheetName: offer.sheetName,
            columns: offer.columns,
            rows: offer.rows,
          ),
        );
      },
    );
  }
}

/// Fetches a named graph entity, then renders it as a navigable 3D graph.
///
/// The controller is created once and disposed with the widget so the camera
/// and navigation history survive rebuilds.
final class _KitGraphRefLoader extends StatefulWidget {
  const _KitGraphRefLoader({
    required this.name,
    required this.caption,
    required this.reader,
    this.sceneFactory,
  });

  final String name;
  final String caption;
  final KitGraphRefReader? reader;
  final GraphSceneFactory? sceneFactory;

  @override
  State<_KitGraphRefLoader> createState() => _KitGraphRefLoaderState();
}

final class _KitGraphRefLoaderState extends State<_KitGraphRefLoader> {
  Future<ChatGraphOffer?>? _fetch;
  KitGraphController? _controller;

  @override
  void initState() {
    super.initState();
    final reader = widget.reader;
    if (reader != null) {
      _fetch = reader(widget.name);
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  KitGraphController _controllerFor(ChatGraphOffer offer) {
    final nodes = [
      for (final node in offer.nodes)
        GraphNode(
          id: node.id,
          label: node.label,
          kind: switch (node.kind) {
            'hub' => GraphNodeKind.hub,
            'entity' => GraphNodeKind.entity,
            'module' => GraphNodeKind.module,
            _ => GraphNodeKind.leaf,
          },
          cluster: node.cluster,
        ),
    ];
    final edges = [
      for (final edge in offer.edges)
        GraphEdge(
          id: edge.id,
          sourceId: edge.sourceId,
          targetId: edge.targetId,
          dotted: edge.dotted,
        ),
    ];

    final existing = _controller;
    if (existing != null) {
      existing.setGraph(nodes: nodes, edges: edges);
      return existing;
    }
    return _controller = KitGraphController(nodes: nodes, edges: edges);
  }

  @override
  Widget build(BuildContext context) {
    if (widget.reader == null) {
      return Text(
        widget.caption,
        key: Key('kit_graph_ref_offline_${widget.name}'),
        style: KitType.bodyMuted,
      );
    }

    return FutureBuilder<ChatGraphOffer?>(
      future: _fetch,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Padding(
            key: Key('kit_graph_ref_loading'),
            padding: EdgeInsets.all(16),
            child: Center(child: CircularProgressIndicator(strokeWidth: 2)),
          );
        }

        final offer = snapshot.data;
        if (offer == null) {
          return Text(
            widget.caption,
            key: Key('kit_graph_ref_missing_${widget.name}'),
            style: KitType.bodyMuted,
          );
        }

        final controller = _controllerFor(offer);
        return SizedBox(
          height: 360,
          child: Column(
            children: [
              Expanded(
                child: KitGraphView(
                  controller: controller,
                  sceneFactory: widget.sceneFactory,
                ),
              ),
              KitGraphNavigator(controller: controller),
            ],
          ),
        );
      },
    );
  }
}
