import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/application_studio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  testWidgets('new application loads the server starter for its chosen key', (
    tester,
  ) async {
    const starter = 'complete server starter for custom';
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://kernel.test'),
      httpClient: MockClient((request) async {
        if (request.url.path == '/applications') {
          return http.Response('[]', 200);
        }
        if (request.url.path == '/applications/custom/template') {
          return http.Response(jsonEncode(starter), 200);
        }
        return http.Response('not found', 404);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: ApplicationLibrary(api: client, changes: ValueNotifier<int>(0)),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('New application'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('new_application_key')),
      'custom',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Create'));
    await tester.pumpAndSettle();
    final field = tester.widget<TextField>(
      find.byKey(const Key('application_source')),
    );
    expect(field.controller!.text, starter);
  });

  testWidgets('editor saves validates and activates an exact source revision', (
    tester,
  ) async {
    final requests = <http.Request>[];
    final transport = MockClient((request) async {
      requests.add(request);
      final path = request.url.path;
      if (request.method == 'GET' && path == '/applications') {
        return http.Response(
          jsonEncode([
            {
              'key': 'ping',
              'sourceRevision': 'source-1',
              'validated': false,
              'activeRevision': null,
              'pendingActivationRevision': null,
            },
          ]),
          200,
        );
      }
      if (request.method == 'GET' && path == '/applications/ping') {
        return http.Response(
          jsonEncode({
            'key': 'ping',
            'sourceRevision': 'source-1',
            'source': 'original source',
            'activeRevision': null,
            'pendingActivationRevision': null,
          }),
          200,
        );
      }
      if (request.method == 'GET' && path == '/applications/ping/files') {
        return http.Response(jsonEncode(['application.cs']), 200);
      }
      if (request.method == 'POST' && path == '/applications/ping/save') {
        expect(jsonDecode(request.body), {
          'source': 'edited complete application',
          'expectedRevision': 'source-1',
        });
        return http.Response(
          jsonEncode({
            'key': 'ping',
            'sourceRevision': 'source-2',
            'source': 'edited complete application',
            'activeRevision': null,
            'pendingActivationRevision': null,
          }),
          200,
        );
      }
      if (request.method == 'POST' && path == '/applications/ping/validate') {
        expect(jsonDecode(request.body), {
          'expectedSourceRevision': 'source-2',
        });
        return http.Response(
          jsonEncode({
            'sourceRevision': 'source-2',
            'succeeded': true,
            'diagnostics': null,
          }),
          200,
        );
      }
      if (request.method == 'POST' && path == '/applications/ping/activate') {
        expect(jsonDecode(request.body), {
          'expectedSourceRevision': 'source-2',
        });
        return http.Response(
          jsonEncode({
            'key': 'ping',
            'sourceRevision': 'source-2',
            'artifactRevision': 'artifact-2',
          }),
          200,
        );
      }
      return http.Response('unexpected ${request.method} $path', 404);
    });
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://kernel.test'),
      httpClient: transport,
    );

    await tester.pumpWidget(
      MaterialApp(
        home: ApplicationLibrary(
          api: client,
          changes: ValueNotifier<int>(0),
          initialKey: 'ping',
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Input signals (comma separated)'), findsNothing);
    expect(find.text('Output signals (comma separated)'), findsNothing);
    await tester.enterText(
      find.byKey(const Key('application_source')),
      'edited complete application',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(OutlinedButton, 'Validate'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(OutlinedButton, 'Activate'));
    await tester.pumpAndSettle();

    expect(
      requests.map((request) => '${request.method} ${request.url.path}'),
      containsAllInOrder([
        'GET /applications',
        'GET /applications/ping',
        'GET /applications/ping/files',
        'POST /applications/ping/save',
        'POST /applications/ping/validate',
        'POST /applications/ping/activate',
      ]),
    );
    expect(
      requests.where((request) => request.url.path == '/applications'),
      hasLength(greaterThanOrEqualTo(3)),
    );
  });

  testWidgets('editor selects and saves a child file at the bundle revision', (
    tester,
  ) async {
    final requests = <http.Request>[];
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://kernel.test'),
      httpClient: MockClient((request) async {
        requests.add(request);
        if (request.url.path == '/applications/ping') {
          return http.Response(
            jsonEncode({
              'key': 'ping',
              'path': 'start.cs',
              'sourceRevision': 'bundle-1',
              'source': 'root source',
            }),
            200,
          );
        }
        if (request.url.path == '/applications/ping/files') {
          return http.Response(jsonEncode(['start.cs', 'ui.cs']), 200);
        }
        if (request.method == 'GET' &&
            request.url.path == '/applications/ping/file') {
          expect(request.url.queryParameters['path'], 'ui.cs');
          return http.Response(
            jsonEncode({
              'key': 'ping',
              'path': 'ui.cs',
              'sourceRevision': 'bundle-1',
              'source': 'ui source',
            }),
            200,
          );
        }
        if (request.method == 'POST' &&
            request.url.path == '/applications/ping/file') {
          expect(jsonDecode(request.body), {
            'path': 'ui.cs',
            'source': 'edited ui source',
            'expectedRevision': 'bundle-1',
          });
          return http.Response(
            jsonEncode({
              'key': 'ping',
              'path': 'ui.cs',
              'sourceRevision': 'bundle-2',
              'source': 'edited ui source',
            }),
            200,
          );
        }
        return http.Response(
          'unexpected ${request.method} ${request.url}',
          404,
        );
      }),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ApplicationEditor(api: client, applicationKey: 'ping'),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('application_file_selector')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('ui.cs').last);
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<TextField>(find.byKey(const Key('application_source')))
          .controller!
          .text,
      'ui source',
    );
    await tester.enterText(
      find.byKey(const Key('application_source')),
      'edited ui source',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(
      requests.map((request) => '${request.method} ${request.url.path}'),
      containsAllInOrder([
        'GET /applications/ping',
        'GET /applications/ping/files',
        'GET /applications/ping/file',
        'POST /applications/ping/file',
      ]),
    );
  });

  testWidgets(
    'editor creates and saves a new child without losing dirty text',
    (tester) async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://kernel.test'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/applications/ping') {
            return http.Response(
              jsonEncode({
                'key': 'ping',
                'path': 'start.cs',
                'sourceRevision': 'bundle-1',
                'source': 'root source',
              }),
              200,
            );
          }
          if (request.url.path == '/applications/ping/files') {
            return http.Response(jsonEncode(['start.cs', 'ui.cs']), 200);
          }
          if (request.method == 'POST' &&
              request.url.path == '/applications/ping/file') {
            expect(jsonDecode(request.body), {
              'path': 'helpers.cs',
              'source': 'helper source',
              'expectedRevision': 'bundle-1',
            });
            return http.Response(
              jsonEncode({
                'key': 'ping',
                'path': 'helpers.cs',
                'sourceRevision': 'bundle-2',
                'source': 'helper source',
              }),
              200,
            );
          }
          return http.Response(
            'unexpected ${request.method} ${request.url}',
            404,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ApplicationEditor(api: client, applicationKey: 'ping'),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(OutlinedButton, 'New file'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('new_application_file_path')),
        'helpers.cs',
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Create'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('application_source')),
        'helper source',
      );

      final selector = tester.widget<DropdownButtonFormField<String>>(
        find.byKey(const Key('application_file_selector')),
      );
      expect(selector.onChanged, isNull);
      expect(
        tester
            .widget<TextField>(find.byKey(const Key('application_source')))
            .controller!
            .text,
        'helper source',
      );

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();
      expect(find.text('helpers.cs'), findsOneWidget);
    },
  );

  testWidgets('saving a new root immediately enables bundle file authoring', (
    tester,
  ) async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://kernel.test'),
      httpClient: MockClient((request) async {
        if (request.url.path == '/applications/fresh/template') {
          return http.Response(jsonEncode('starter'), 200);
        }
        if (request.url.path == '/applications/fresh/save') {
          return http.Response(
            jsonEncode({
              'key': 'fresh',
              'path': 'start.cs',
              'sourceRevision': 'bundle-1',
              'source': 'starter',
            }),
            200,
          );
        }
        return http.Response('unexpected', 404);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ApplicationEditor(
            api: client,
            applicationKey: 'fresh',
            isNew: true,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('application_file_selector')), findsOneWidget);
    expect(find.text('start.cs'), findsOneWidget);
    expect(
      tester
          .widget<OutlinedButton>(
            find.widgetWithText(OutlinedButton, 'New file'),
          )
          .onPressed,
      isNotNull,
    );
  });

  testWidgets('dirty editor blocks switching to another application', (
    tester,
  ) async {
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://kernel.test'),
      httpClient: MockClient((request) async {
        if (request.url.path == '/applications') {
          return http.Response(
            jsonEncode([
              {'key': 'alpha', 'sourceRevision': 'a1', 'validated': false},
              {'key': 'beta', 'sourceRevision': 'b1', 'validated': false},
            ]),
            200,
          );
        }
        if (request.url.path == '/applications/alpha') {
          return http.Response(
            jsonEncode({
              'key': 'alpha',
              'path': 'start.cs',
              'sourceRevision': 'a1',
              'source': 'alpha',
            }),
            200,
          );
        }
        if (request.url.path == '/applications/alpha/files') {
          return http.Response(jsonEncode(['start.cs']), 200);
        }
        return http.Response('unexpected', 404);
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        home: ApplicationLibrary(
          api: client,
          changes: ValueNotifier<int>(0),
          initialKey: 'alpha',
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('application_source')),
      'unsaved alpha',
    );
    await tester.tap(find.byKey(const ValueKey('application_beta')));
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<TextField>(find.byKey(const Key('application_source')))
          .controller!
          .text,
      'unsaved alpha',
    );
  });

  testWidgets(
    'declared examples must pass for the current revision before activation',
    (tester) async {
      var runs = 0;
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://kernel.test'),
        httpClient: MockClient((request) async {
          if (request.url.path == '/applications/ping') {
            return http.Response(
              jsonEncode({
                'key': 'ping',
                'path': 'start.cs',
                'sourceRevision': 'bundle-1',
                'source': 'source',
              }),
              200,
            );
          }
          if (request.url.path == '/applications/ping/files') {
            return http.Response(
              jsonEncode(['acceptance.json', 'start.cs']),
              200,
            );
          }
          if (request.method == 'GET' &&
              request.url.path == '/applications/ping/scenarios') {
            return http.Response(
              jsonEncode({
                'sourceRevision': 'bundle-1',
                'artifactRevision': 'artifact-1',
                'passed': false,
                'examples': [
                  {
                    'name': 'ping',
                    'passed': false,
                    'actualJson': '"wrong"',
                    'error': 'expected "pong"',
                  },
                ],
              }),
              200,
            );
          }
          if (request.url.path == '/applications/ping/validate') {
            return http.Response(
              jsonEncode({'sourceRevision': 'bundle-1', 'succeeded': true}),
              200,
            );
          }
          if (request.url.path == '/applications/ping/scenarios/run') {
            runs++;
            expect(jsonDecode(request.body), {
              'expectedSourceRevision': 'bundle-1',
            });
            return http.Response(
              jsonEncode({
                'sourceRevision': 'bundle-1',
                'artifactRevision': 'artifact-1',
                'passed': true,
                'examples': [
                  {
                    'name': 'ping',
                    'passed': true,
                    'actualJson': '"pong"',
                    'error': null,
                  },
                ],
              }),
              200,
            );
          }
          return http.Response(
            'unexpected ${request.method} ${request.url}',
            404,
          );
        }),
      );
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: ApplicationEditor(api: client, applicationKey: 'ping'),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('expected "pong"'), findsOneWidget);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Validate'));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'Activate'),
            )
            .onPressed,
        isNull,
      );
      await tester.tap(find.widgetWithText(OutlinedButton, 'Run examples'));
      await tester.pumpAndSettle();
      expect(runs, 1);
      expect(find.textContaining('ping: passed'), findsOneWidget);
      expect(
        tester
            .widget<OutlinedButton>(
              find.widgetWithText(OutlinedButton, 'Activate'),
            )
            .onPressed,
        isNotNull,
      );
    },
  );
}
