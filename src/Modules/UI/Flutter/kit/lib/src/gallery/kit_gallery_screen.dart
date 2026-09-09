import 'package:flutter/material.dart';

import '../lumen/lumen_controls.dart';
import '../lumen/lumen_palette.dart';
import '../models/kit_part.dart';
import '../theme/kit_theme.dart';
import 'kit_gallery_preview.dart';

/// Offline component catalog. Example state never changes the brain.
final class KitGalleryScreen extends StatefulWidget {
  const KitGalleryScreen({super.key, this.onButtonPressed});
  final ValueChanged<KitButtonPart>? onButtonPressed;
  @override
  State<KitGalleryScreen> createState() => _KitGalleryScreenState();
}

final class _KitGalleryScreenState extends State<KitGalleryScreen> {
  final _search = TextEditingController();
  String _category = 'All components';
  GalleryEntry _selected = galleryEntries.first;
  bool _showDetail = false;
  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Theme(
    data: KitTheme.light(),
    child: KitThemeScope(
      child: Material(
        key: const Key('kit_gallery_screen'),
        color: LumenPalette.background,
        child: LayoutBuilder(
          builder: (context, constraints) {
            final wide = constraints.maxWidth >= 800;
            return Column(
              children: [
                Padding(
                  padding: EdgeInsets.fromLTRB(wide ? 32 : 20, 24, 20, 20),
                  child: Row(
                    children: [
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: LumenPalette.accentSoft,
                          borderRadius: BorderRadius.circular(16),
                        ),
                        child: const Icon(
                          Icons.widgets_outlined,
                          color: LumenPalette.accent,
                        ),
                      ),
                      const SizedBox(width: 14),
                      const Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'UI Kit',
                              style: TextStyle(
                                fontSize: 26,
                                fontWeight: FontWeight.w600,
                                color: LumenPalette.ink,
                                letterSpacing: -0.8,
                              ),
                            ),
                            Text(
                              'The building blocks of DigitalBrain',
                              style: TextStyle(
                                color: LumenPalette.muted,
                                fontSize: 13,
                              ),
                            ),
                          ],
                        ),
                      ),
                      if (wide)
                        const Text(
                          'LUMEN / COMPONENT LIBRARY',
                          style: TextStyle(
                            fontSize: 10,
                            letterSpacing: 1.4,
                            color: LumenPalette.muted,
                          ),
                        ),
                    ],
                  ),
                ),
                const Divider(height: 1),
                Expanded(
                  child: wide
                      ? Row(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            SizedBox(width: 290, child: _catalog()),
                            const VerticalDivider(width: 1),
                            Expanded(child: _detail(false)),
                          ],
                        )
                      : _showDetail
                      ? _detail(true)
                      : _catalog(),
                ),
              ],
            );
          },
        ),
      ),
    ),
  );

  Widget _catalog() {
    final query = _search.text.trim().toLowerCase();
    final matches = galleryEntries
        .where(
          (entry) =>
              (_category == 'All components' || entry.category == _category) &&
              '${entry.title} ${entry.api} ${entry.description} ${entry.category}'
                  .toLowerCase()
                  .contains(query),
        )
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(18, 20, 18, 12),
          child: TextField(
            key: const Key('gallery_search'),
            controller: _search,
            onChanged: (_) => setState(() {}),
            decoration: InputDecoration(
              hintText: 'Search components',
              prefixIcon: const Icon(Icons.search, size: 20),
              isDense: true,
              suffixIcon: query.isEmpty
                  ? null
                  : IconButton(
                      tooltip: 'Clear search',
                      icon: const Icon(Icons.close, size: 18),
                      onPressed: () => setState(_search.clear),
                    ),
            ),
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 18),
          child: DropdownButtonFormField<String>(
            key: const Key('gallery_category'),
            initialValue: _category,
            isExpanded: true,
            decoration: const InputDecoration(
              labelText: 'Category',
              isDense: true,
            ),
            items:
                [
                      'All components',
                      ...galleryEntries.map((e) => e.category).toSet(),
                    ]
                    .map(
                      (c) => DropdownMenuItem(
                        value: c,
                        child: Text(c, style: const TextStyle(fontSize: 13)),
                      ),
                    )
                    .toList(),
            onChanged: (value) => setState(() => _category = value!),
          ),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 18, 20, 8),
          child: Text(
            '${matches.length} components',
            style: const TextStyle(color: LumenPalette.muted, fontSize: 12),
          ),
        ),
        Expanded(
          child: matches.isEmpty
              ? const Center(
                  child: Padding(
                    padding: EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.search_off,
                          color: LumenPalette.muted,
                          size: 32,
                        ),
                        SizedBox(height: 12),
                        Text('No components found'),
                        SizedBox(height: 6),
                        Text(
                          'Try a name such as chart, or another category.',
                          textAlign: TextAlign.center,
                          style: TextStyle(color: LumenPalette.muted),
                        ),
                      ],
                    ),
                  ),
                )
              : ListView.builder(
                  padding: const EdgeInsets.fromLTRB(10, 0, 10, 24),
                  itemCount: matches.length,
                  itemBuilder: (context, index) {
                    final entry = matches[index];
                    return Padding(
                      padding: const EdgeInsets.only(bottom: 4),
                      child: ListTile(
                        key: Key('gallery_entry_${entry.id}'),
                        selected: _selected.id == entry.id,
                        selectedTileColor: LumenPalette.accentSoft,
                        selectedColor: LumenPalette.accent,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                        leading: Icon(entry.icon, size: 22),
                        title: Text(
                          entry.title,
                          style: const TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        subtitle: Text(
                          entry.category,
                          style: const TextStyle(fontSize: 11),
                        ),
                        trailing: const Icon(Icons.chevron_right, size: 18),
                        onTap: () => setState(() {
                          _selected = entry;
                          _showDetail = true;
                        }),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }

  Widget _detail(bool narrow) => ListView(
    key: ValueKey('gallery_detail_${_selected.id}'),
    padding: EdgeInsets.all(narrow ? 20 : 32),
    children: [
      if (narrow)
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton.icon(
            key: const Key('gallery_back'),
            onPressed: () => setState(() => _showDetail = false),
            icon: const Icon(Icons.arrow_back, size: 18),
            label: const Text('All components'),
          ),
        ),
      Text(
        _selected.category.toUpperCase(),
        style: const TextStyle(
          color: LumenPalette.accent,
          fontSize: 11,
          letterSpacing: 1.4,
          fontWeight: FontWeight.w600,
        ),
      ),
      const SizedBox(height: 12),
      Text(
        _selected.title,
        style: const TextStyle(
          fontSize: 32,
          fontWeight: FontWeight.w600,
          letterSpacing: -1,
          color: LumenPalette.ink,
        ),
      ),
      const SizedBox(height: 10),
      Text(
        _selected.description,
        style: const TextStyle(
          fontSize: 15,
          height: 1.55,
          color: LumenPalette.muted,
        ),
      ),
      const SizedBox(height: 24),
      GalleryPreview(
        key: ValueKey(_selected.id),
        entry: _selected,
        onButtonPressed: widget.onButtonPressed,
      ),
      const SizedBox(height: 24),
      LumenSurface(
        elevated: false,
        radius: 16,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Usage notes',
              style: TextStyle(
                fontWeight: FontWeight.w600,
                color: LumenPalette.ink,
              ),
            ),
            const SizedBox(height: 10),
            Text(
              _selected.notes,
              style: const TextStyle(color: LumenPalette.muted, height: 1.5),
            ),
            const SizedBox(height: 18),
            const Text(
              'COMPONENT API',
              style: TextStyle(
                fontSize: 10,
                letterSpacing: 1,
                color: LumenPalette.muted,
              ),
            ),
            const SizedBox(height: 8),
            SelectableText(
              _selected.api,
              style: const TextStyle(
                fontFamily: KitType.monoFamily,
                fontSize: 12,
                color: LumenPalette.accent,
                height: 1.6,
              ),
            ),
          ],
        ),
      ),
      const SizedBox(height: 24),
      const Text(
        'Offline examples · Changes here stay in this preview.',
        style: TextStyle(fontSize: 12, color: LumenPalette.muted),
      ),
    ],
  );
}
