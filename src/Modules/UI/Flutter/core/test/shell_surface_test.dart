import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test(
    'scripted surface-opened wire frame reloads Home at shared cursor zero',
    () {
      const frames = '''
event: surface-opened
data: {"sequence":0,"surfaceKey":"home","title":"One brain","commandId":"746efc777e5c446e93d5f4ded819a333","surface":"surface:dev/desk"}

''';
      final parser = SseSceneOpenedParser();
      final events = <SceneOpenedEvent>[
        for (final line in frames.split('\n')) ...parser.addLine(line),
        ...parser.flush(),
      ];

      expect(events, hasLength(1));
      expect(events.single.sequence, 0);
      expect(events.single.sceneKey, 'home');
      expect(events.single.shell, 'surface:dev/desk');
    },
  );

  test(
    'ShellSurfaceController projects SceneOpened without dropping prior scenes',
    () {
      final surface = ShellSurfaceController();

      surface.apply(
        const SceneOpenedEvent(
          sequence: 1,
          sceneKey: 'home',
          title: 'Home',
          commandId: 'a',
          shell: 'shell:dev/desk',
        ),
      );
      surface.apply(
        const SceneOpenedEvent(
          sequence: 2,
          sceneKey: 'countdown',
          title: 'Countdown',
          commandId: 'b',
          shell: 'shell:dev/desk',
        ),
      );

      expect(surface.scenes.map((s) => s.sceneKey), ['home', 'countdown']);
      expect(surface.latest?.title, 'Countdown');
      expect(surface.latest?.sequence, 2);
    },
  );

  test(
    'ShellSurfaceController replaces same sceneKey with newer projection',
    () {
      final surface = ShellSurfaceController();

      surface.apply(
        const SceneOpenedEvent(
          sequence: 1,
          sceneKey: 'home',
          title: 'Home',
          commandId: 'a',
          shell: 'shell:dev/desk',
        ),
      );
      surface.apply(
        const SceneOpenedEvent(
          sequence: 4,
          sceneKey: 'home',
          title: 'Home again',
          commandId: 'c',
          shell: 'shell:dev/desk',
        ),
      );

      expect(surface.scenes, hasLength(1));
      expect(surface.latest?.title, 'Home again');
      expect(surface.latest?.sequence, 4);
    },
  );

  test(
    'latest is most recently applied even when replace is not list-last',
    () {
      final surface = ShellSurfaceController();

      surface.apply(
        const SceneOpenedEvent(
          sequence: 1,
          sceneKey: 'home',
          title: 'Home',
          commandId: 'a',
          shell: 'shell:dev/desk',
        ),
      );
      surface.apply(
        const SceneOpenedEvent(
          sequence: 2,
          sceneKey: 'countdown',
          title: 'Countdown',
          commandId: 'b',
          shell: 'shell:dev/desk',
        ),
      );
      final reopened = surface.apply(
        const SceneOpenedEvent(
          sequence: 5,
          sceneKey: 'home',
          title: 'Home refreshed',
          commandId: 'c',
          shell: 'shell:dev/desk',
        ),
      );

      expect(surface.scenes.map((s) => s.sceneKey), ['home', 'countdown']);
      expect(reopened.title, 'Home refreshed');
      expect(surface.latest?.sceneKey, 'home');
      expect(surface.latest?.title, 'Home refreshed');
      expect(surface.latest?.sequence, 5);
    },
  );

  test('apply returns the projected view for the host log path', () {
    final surface = ShellSurfaceController();
    final view = surface.apply(
      const SceneOpenedEvent(
        sequence: 9,
        sceneKey: 'settings',
        title: 'Settings',
        commandId: 'z',
        shell: 'shell:dev/desk',
      ),
    );

    expect(view.sceneKey, 'settings');
    expect(view.title, 'Settings');
    expect(view.sequence, 9);
    expect(identical(view, surface.latest), isTrue);
  });

  test('SseSceneOpenedParser reads scene-opened frames without restart', () {
    const frames = '''
: connected

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"c","shell":"shell:dev/desk"}

id: 5
event: scene-opened
data: {"sequence":5,"sceneKey":"countdown","title":"Countdown","commandId":"d","shell":"shell:dev/desk"}

''';

    final parser = SseSceneOpenedParser();
    final events = <SceneOpenedEvent>[
      for (final line in frames.split('\n')) ...parser.addLine(line),
      ...parser.flush(),
    ];
    expect(events, hasLength(2));
    expect(events[0].sceneKey, 'home');
    expect(events[1].title, 'Countdown');

    final surface = ShellSurfaceController();
    for (final event in events) {
      surface.apply(event);
    }
    expect(surface.scenes.map((s) => '${s.sceneKey}:${s.title}'), [
      'home:Home',
      'countdown:Countdown',
    ]);
  });

  test('SseSceneOpenedParser ignores non scene-opened events and bad JSON', () {
    const frames = '''
: connected

event: other
data: {"sequence":1,"sceneKey":"nope","title":"Nope","commandId":"x","shell":"shell:dev/desk"}

event: scene-opened
data: not-json

event: scene-opened
data: {"sequence":2,"sceneKey":"home","title":"Home","commandId":"y","shell":"shell:dev/desk"}

''';

    final parser = SseSceneOpenedParser();
    final events = <SceneOpenedEvent>[
      for (final line in frames.split('\n')) ...parser.addLine(line),
      ...parser.flush(),
    ];
    expect(events, hasLength(1));
    expect(events.single.sceneKey, 'home');
    expect(events.single.sequence, 2);
  });

  test(
    'SseSceneOpenedParser fails closed without explicit event: scene-opened',
    () {
      const frames = '''
: connected

data: {"sequence":1,"sceneKey":"orphan","title":"Orphan","commandId":"x","shell":"shell:dev/desk"}

event: message
data: {"sequence":2,"sceneKey":"message","title":"Message","commandId":"y","shell":"shell:dev/desk"}

event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"z","shell":"shell:dev/desk"}

''';

      final parser = SseSceneOpenedParser();
      final events = <SceneOpenedEvent>[
        for (final line in frames.split('\n')) ...parser.addLine(line),
        ...parser.flush(),
      ];
      expect(events, hasLength(1));
      expect(events.single.sceneKey, 'home');
      expect(events.single.sequence, 3);
    },
  );

  test(
    'SSE multi-event feed projects live into one ShellSurfaceController without restart',
    () {
      const frames = '''
: connected

id: 3
event: scene-opened
data: {"sequence":3,"sceneKey":"home","title":"Home","commandId":"c","shell":"shell:dev/desk"}

id: 5
event: scene-opened
data: {"sequence":5,"sceneKey":"countdown","title":"Countdown","commandId":"d","shell":"shell:dev/desk"}

id: 7
event: scene-opened
data: {"sequence":7,"sceneKey":"home","title":"Home refreshed","commandId":"e","shell":"shell:dev/desk"}

''';

      final parser = SseSceneOpenedParser();
      final surface = ShellSurfaceController();
      final parserIdentity = identityHashCode(parser);
      final surfaceIdentity = identityHashCode(surface);
      final liveSnapshots = <List<String>>[];

      for (final line in frames.split('\n')) {
        for (final event in parser.addLine(line)) {
          surface.apply(event);
          liveSnapshots.add(
            surface.scenes.map((s) => '${s.sceneKey}:${s.title}').toList(),
          );
        }
      }
      for (final event in parser.flush()) {
        surface.apply(event);
        liveSnapshots.add(
          surface.scenes.map((s) => '${s.sceneKey}:${s.title}').toList(),
        );
      }

      expect(identityHashCode(parser), parserIdentity);
      expect(identityHashCode(surface), surfaceIdentity);
      expect(liveSnapshots, [
        ['home:Home'],
        ['home:Home', 'countdown:Countdown'],
        ['home:Home refreshed', 'countdown:Countdown'],
      ]);
      expect(surface.latest?.sequence, 7);
      expect(surface.latest?.sceneKey, 'home');
    },
  );
}
