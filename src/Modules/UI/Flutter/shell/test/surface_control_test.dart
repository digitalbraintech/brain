import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/graph_home_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('persisted enabled button sends its exact activation', (
    tester,
  ) async {
    String? activated;
    await tester.pumpWidget(
      MaterialApp(
        home: GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          surfaceKey: 'home',
          surfaceRoot: const SurfaceComponent(
            kind: 'button',
            key: 'refresh',
            properties: {'intent': 'refresh', 'enabled': 'true'},
          ),
          onActivateControl: (surfaceKey, controlId, intent) async {
            activated = '$surfaceKey/$controlId/$intent';
          },
        ),
      ),
    );

    await tester.tap(find.byKey(const ValueKey('surface-control-refresh')));
    await tester.pump();

    expect(activated, 'home/refresh/refresh');
  });

  testWidgets('persisted disabled button cannot be activated', (tester) async {
    var activations = 0;
    await tester.pumpWidget(
      MaterialApp(
        home: GraphHomeScreen(
          chatName: 'main',
          turns: const [],
          surfaceKey: 'home',
          surfaceRoot: const SurfaceComponent(
            kind: 'button',
            key: 'refresh',
            properties: {'intent': 'refresh', 'enabled': 'false'},
          ),
          onActivateControl: (_, _, _) async => activations++,
        ),
      ),
    );

    final button = tester.widget<FilledButton>(
      find.byKey(const ValueKey('surface-control-refresh')),
    );
    expect(button.onPressed, isNull);
    expect(activations, 0);
  });
}
