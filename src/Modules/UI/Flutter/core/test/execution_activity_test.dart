import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  test(
    'selected results retain stable event identity from the renderer',
    () async {
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: MockClient((request) async {
          expect(request.url.path, '/surfaces/desk/activities/a/results');
          return http.Response(
            jsonEncode([
              {
                'sequence': 123,
                'eventId': 'signal-id',
                'fromUser': false,
                'text': 'Research result',
                'commandId': 'cmd',
                'signal': 'Responded',
                'neuronId': 'assistant:ino',
                'caller': 'assistant:ino',
                'correlationId': 'a',
                'timestamp': '2026-09-06T10:00:00Z',
              },
            ]),
            200,
          );
        }),
      );
      final result = await client.readActivityResults(
        surfaceName: 'desk',
        activityId: 'a',
      );
      expect(result.single.eventId, 'signal-id');
      expect(result.single.text, 'Research result');
      client.close();
    },
  );
  test(
    'renderer activity stream ignores older versions and preserves terminal state',
    () async {
      Map<String, Object?> item(int version, String status) => {
        'id': 'a',
        'correlationId': 'a',
        'rootSignalId': 's',
        'triggerName': 'UserMessaged',
        'title': 'Research Orleans',
        'status': status,
        'version': version,
        'startedAt': '2026-09-06T10:00:00Z',
        'updatedAt': '2026-09-06T10:01:00Z',
      };
      final client = DigitalBrainUiClient(
        baseUri: Uri.parse('http://localhost:5080'),
        httpClient: MockClient((request) async {
          expect(request.url.path, '/surfaces/desk/activities/events');
          return http.Response(
            'event: snapshot\ndata: ${jsonEncode({
              'activities': [item(2, 'completed')],
            })}\n\n'
            'event: activity\ndata: ${jsonEncode(item(1, 'running'))}\n\n'
            'event: activity\ndata: ${jsonEncode(item(3, 'completed'))}\n\n',
            200,
          );
        }),
      );
      final updates = await client
          .watchActivities(surfaceName: 'desk')
          .toList();
      expect(updates.map((items) => items.single.version), [2, 3]);
      expect(
        updates.every((items) => items.single.status == 'completed'),
        isTrue,
      );
      client.close();
    },
  );
  test(
    'execution activity preserves explicit lifecycle and causal evidence',
    () {
      final value = ExecutionActivity.fromJson({
        'id': 'a',
        'correlationId': 'a',
        'rootSignalId': 's',
        'triggerName': 'UserMessaged',
        'title': 'Research Orleans',
        'status': 'observed',
        'commandId': 'command',
        'startedAt': '2026-09-06T10:00:00Z',
        'updatedAt': '2026-09-06T10:01:00Z',
        'participantNeuronIds': ['source', 'target'],
        'events': [
          {
            'operationId': 'op',
            'signalId': 'child',
            'causationId': 's',
            'sourceNeuronId': 'source',
            'targetNeuronId': 'target',
            'signalType': 'Note',
            'phase': 'accepted',
            'timestamp': '2026-09-06T10:01:00Z',
            'behaviorRevision': 'r1',
          },
        ],
      });
      expect(value.status, 'observed');
      expect(value.commandId, 'command');
      expect(value.events.single.causationId, 's');
      expect(value.events.single.behaviorRevision, 'r1');
    },
  );
}
