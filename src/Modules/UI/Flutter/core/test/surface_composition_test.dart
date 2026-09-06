import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:test/test.dart';

void main() {
  test('surface retains a script-authored split and its component properties', () {
    final state = KitSurfaceState.fromJson({
      'scenes': [
        {
          'surfaceKey': 'home',
          'title': 'My brain',
          'root': {
            'kind': 'split',
            'properties': {'graphFraction': '0.61'},
            'children': [
              {'kind': 'chat', 'key': 'input', 'properties': {'voice': 'true'}},
              {'kind': 'brain-graph', 'key': 'brain'},
            ],
          },
        },
      ],
    });
    final root = state.scenes.single.root!;
    expect(root.kind, 'split');
    expect(root.properties['graphFraction'], '0.61');
    expect(root.children.map((child) => child.kind), ['chat', 'brain-graph']);
    expect(root.children.first.properties['voice'], 'true');
  });
}
