import 'dart:async';
import 'dart:convert';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:http/http.dart' as http;
import 'package:test/test.dart';

String frame(int sequence, String title) =>
    'event: surface-opened\ndata: ${jsonEncode({'sequence': sequence, 'surfaceKey': 'home', 'title': title, 'commandId': title, 'surface': 'surface:desk'})}\n\n';

final class _Transport extends http.BaseClient {
  _Transport(this.handle);
  final Future<http.StreamedResponse> Function(http.BaseRequest) handle;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) =>
      handle(request);
}

void main() {
  test(
    'reconnect resumes principal cursor and replays shared scenes after failure',
    () async {
      final cursors = <String?>[];
      final errors = <Object>[];
      final events = <SceneOpenedEvent>[];
      final done = Completer<void>();
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: _Transport((request) async {
          cursors.add(request.url.queryParameters['afterSequence']);
          final status = cursors.length == 2 ? 503 : 200;
          final body = switch (cursors.length) {
            1 => frame(8, 'principal') + frame(0, 'shared-before'),
            2 => '',
            _ =>
              frame(7, 'stale-principal') +
                  frame(0, 'shared-after') +
                  frame(9, 'new-principal'),
          };
          return http.StreamedResponse(Stream.value(utf8.encode(body)), status);
        }),
      );
      final subscription = client
          .watchShellEvents(shellName: 'desk', afterSequence: 5)
          .listen(
            (event) {
              events.add(event);
              if (events.length == 4) done.complete();
            },
            onError: errors.add,
            onDone: () {
              if (!done.isCompleted) done.complete();
            },
          );
      await done.future.timeout(const Duration(seconds: 5));
      await subscription.cancel();
      expect(events.map((event) => event.title), [
        'principal',
        'shared-before',
        'shared-after',
        'new-principal',
      ]);
      expect(cursors, ['5', '8', '8']);
      expect(errors, hasLength(1));
    },
  );

  test('cancelling while disconnected removes the pending reconnect', () async {
    var requests = 0;
    final failed = Completer<void>();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost:5080'),
      httpClient: _Transport((_) async {
        requests++;
        throw http.ClientException('offline');
      }),
    );
    final subscription = client
        .watchShellEvents(shellName: 'desk')
        .listen(
          (_) {},
          onError: (_) {
            if (!failed.isCompleted) failed.complete();
          },
        );
    await failed.future;
    await subscription.cancel();
    await Future<void>.delayed(const Duration(milliseconds: 650));
    expect(requests, 1);
  });

  test(
    'cancelling aborts an HTTP connection still waiting for headers',
    () async {
      final requestStarted = Completer<void>();
      final aborted = Completer<void>();
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: _Transport((request) async {
          requestStarted.complete();
          if (request is http.AbortableRequest) {
            await request.abortTrigger;
            aborted.complete();
          }
          return http.StreamedResponse(const Stream.empty(), 200);
        }),
      );
      final subscription = client
          .watchShellEvents(shellName: 'desk')
          .listen((_) {});
      await requestStarted.future;
      await subscription.cancel();
      expect(aborted.isCompleted, isTrue);
    },
  );
}
