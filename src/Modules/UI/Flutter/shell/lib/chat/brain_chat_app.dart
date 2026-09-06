import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:flutter/material.dart';

import '../user_actions/user_action_card.dart';
import 'brain_workspace.dart';
import 'chat_contracts.dart';

export 'chat_contracts.dart';

final class BrainChatApp extends StatelessWidget {
  const BrainChatApp({
    super.key,
    required this.chatName,
    this.turns,
    this.onSend,
    this.onStream,
    this.onStreamVoice,
    this.onAttachmentTap,
    this.onOpenSignIn,
    this.kernelBaseUri,
    this.onCancelTurn,
    this.onReadChart,
    this.onReadImageBytes,
    this.onReadSpreadsheet,
    this.onReadGraph,
    this.onReadSurface,
    this.onReadBrain,
    this.onWatchBrain,
    this.behaviorStudio,
    this.onSetBrainSubscription,
    this.graphSceneFactory,
    this.userActions = const [],
    this.statusMessage,
  });

  final String chatName;
  final Stream<ChatTurnEvent>? turns;
  final SendMessage? onSend;
  final StreamMessage? onStream;
  final StreamVoice? onStreamVoice;
  final VoidCallback? onAttachmentTap;
  final OpenUrl? onOpenSignIn;
  final Uri? kernelBaseUri;
  final CancelChatTurn? onCancelTurn;
  final ReadChart? onReadChart;
  final ReadImageBytes? onReadImageBytes;
  final ReadSpreadsheet? onReadSpreadsheet;
  final ReadGraph? onReadGraph;
  final ReadSurface? onReadSurface;
  final ReadBrain? onReadBrain;
  final WatchBrain? onWatchBrain;
  final BehaviorStudioApi? behaviorStudio;
  final SetBrainSubscription? onSetBrainSubscription;
  final GraphSceneFactory? graphSceneFactory;
  final List<UserActionCardModel> userActions;
  final String? statusMessage;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: KitTheme.light(),
      builder: (context, child) => KitThemeScope(child: child!),
      home: BrainWorkspace(
        chatName: chatName,
        turns: turns,
        onSend: onSend,
        onStream: onStream,
        onStreamVoice: onStreamVoice,
        onAttachmentTap: onAttachmentTap,
        onOpenSignIn: onOpenSignIn,
        kernelBaseUri: kernelBaseUri,
        onCancelTurn: onCancelTurn,
        onReadChart: onReadChart,
        onReadImageBytes: onReadImageBytes,
        onReadSpreadsheet: onReadSpreadsheet,
        onReadGraph: onReadGraph,
        onReadSurface: onReadSurface,
        onReadBrain: onReadBrain,
        onWatchBrain: onWatchBrain,
        behaviorStudio: behaviorStudio,
        onSetBrainSubscription: onSetBrainSubscription,
        graphSceneFactory: graphSceneFactory,
        userActions: userActions,
        statusMessage: statusMessage,
      ),
    );
  }
}
