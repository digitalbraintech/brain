import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

import 'lumen_palette.dart';

enum NeuronIconKind {
  assistant,
  conversation,
  execution,
  gmail,
  salesforce,
  aspire,
  github,
  review,
  architecture,
  quality,
  document,
  search,
  repository,
  memory,
  clock,
  generic;

  /// Only known presentation keys may select a bundled icon. Never interpret a
  /// server value as an asset path, URL, or a substring of a provider name.
  static NeuronIconKind fromKey(String? key) {
    for (final kind in values) {
      if (kind.name == key) return kind;
    }
    return generic;
  }
}

/// One allowlisted local icon vocabulary for graph tiles and their inspectors.
final class NeuronIcon extends StatelessWidget {
  const NeuronIcon({
    super.key,
    required this.kind,
    this.size = 30,
    this.color,
    this.semanticLabel,
  });

  final NeuronIconKind kind;
  final double size;
  final Color? color;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final asset = _brandAssets[kind];
    if (asset != null) {
      return SvgPicture.asset(
        asset,
        package: 'digitalbrain_ui_kit',
        width: size,
        height: size,
        semanticsLabel: semanticLabel ?? kind.name,
      );
    }
    return Icon(
      switch (kind) {
        NeuronIconKind.assistant => Icons.auto_awesome_rounded,
        NeuronIconKind.conversation => Icons.chat_bubble_outline_rounded,
        NeuronIconKind.execution => Icons.bolt_rounded,
        NeuronIconKind.document => Icons.description_outlined,
        NeuronIconKind.search => Icons.search_rounded,
        NeuronIconKind.repository => Icons.code_rounded,
        NeuronIconKind.review => Icons.fact_check_outlined,
        NeuronIconKind.architecture => Icons.account_tree_outlined,
        NeuronIconKind.quality => Icons.rule_folder_outlined,
        NeuronIconKind.memory => Icons.inventory_2_outlined,
        NeuronIconKind.clock => Icons.schedule_rounded,
        _ => Icons.hub_outlined,
      },
      size: size,
      color: color ?? LumenPalette.accent,
      semanticLabel: semanticLabel,
    );
  }

  static const _brandAssets = <NeuronIconKind, String>{
    NeuronIconKind.gmail: 'assets/brands/gmail.svg',
    NeuronIconKind.salesforce: 'assets/brands/salesforce.svg',
    NeuronIconKind.aspire: 'assets/brands/aspire-icon-32.svg',
    NeuronIconKind.github: 'assets/brands/github.svg',
  };
}
