import 'package:digitalbrain_ui_kit/digitalbrain_ui_kit.dart';
import 'package:digitalbrain_ui_kit/src/gallery/kit_gallery_preview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> open(WidgetTester tester, Size size) async {
    await tester.binding.setSurfaceSize(size);
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      MaterialApp(
        theme: KitTheme.light(),
        home: const Scaffold(body: KitGalleryScreen()),
      ),
    );
    await tester.pump();
    expect(find.byKey(const Key('gallery_search')), findsOneWidget);
  }

  testWidgets(
    'search opens a focused chart and switches its real empty state',
    (tester) async {
      await open(tester, const Size(1200, 900));
      await tester.enterText(find.byKey(const Key('gallery_search')), 'chart');
      await tester.pump();
      await tester.tap(find.byKey(const Key('gallery_entry_chart')));
      await tester.pump();
      expect(find.byType(KitChart), findsOneWidget);
      expect(find.byType(KitSheet), findsNothing);
      await tester.tap(find.byKey(const Key('gallery_state_Empty')));
      await tester.pump();
      expect(find.text('No series'), findsOneWidget);
    },
  );

  testWidgets(
    'button preview invokes its action and disabled state prevents it',
    (tester) async {
      await open(tester, const Size(1200, 900));
      await tester.enterText(
        find.byKey(const Key('gallery_search')),
        'KitButton',
      );
      await tester.pump();
      await tester.tap(find.byKey(const Key('gallery_entry_button')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('kit_button_gallery-action')));
      await tester.pump(const Duration(milliseconds: 200));
      expect(find.text('Action invoked 1 time'), findsOneWidget);
      await tester.tap(find.byKey(const Key('gallery_state_Disabled')));
      await tester.pump();
      expect(
        tester.widget<KitButton>(find.byType(KitButton)).onPressed,
        isNull,
      );
    },
  );

  testWidgets('narrow catalog recovers from empty search and opens detail', (
    tester,
  ) async {
    await open(tester, const Size(400, 850));
    await tester.enterText(
      find.byKey(const Key('gallery_search')),
      'no-such-component',
    );
    await tester.pump();
    expect(find.text('No components found'), findsOneWidget);
    await tester.enterText(find.byKey(const Key('gallery_search')), 'KitSheet');
    await tester.pump();
    await tester.tap(find.byKey(const Key('gallery_entry_sheet')));
    await tester.pump();
    expect(find.byType(KitSheet), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.tap(find.byKey(const Key('gallery_back')));
    await tester.pump();
    expect(find.byKey(const Key('gallery_search')), findsOneWidget);
  });

  for (final width in [400.0, 1200.0]) {
    testWidgets('supported preview states fit a $width pixel surface', (
      tester,
    ) async {
      await tester.binding.setSurfaceSize(Size(width, 900));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      for (final entry in galleryEntries) {
        await tester.pumpWidget(
          MaterialApp(
            theme: KitTheme.light(),
            home: Scaffold(
              body: KitThemeScope(
                child: SingleChildScrollView(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: GalleryPreview(
                      key: ValueKey(entry.id),
                      entry: entry,
                    ),
                  ),
                ),
              ),
            ),
          ),
        );
        await tester.pump(const Duration(milliseconds: 200));
        expect(
          tester.takeException(),
          isNull,
          reason: '${entry.title}: initial',
        );
        for (final state in entry.states) {
          await tester.tap(find.byKey(Key('gallery_state_$state')));
          await tester.pump(const Duration(milliseconds: 200));
          expect(
            tester.takeException(),
            isNull,
            reason: '${entry.title}: $state',
          );
        }
        await tester.pumpWidget(const SizedBox());
        await tester.pump(const Duration(milliseconds: 200));
      }
    });
  }
}
