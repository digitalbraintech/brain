import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

import '../../models/kit_part.dart';
import '../../theme/kit_theme.dart';

/// Product button control. Used on surfaces and inside chat CustomMessage rows.
final class KitButton extends StatelessWidget {
  const KitButton({
    super.key,
    required this.part,
    this.onPressed,
    this.dense = false,
  });

  final KitButtonPart part;
  final ValueChanged<KitButtonPart>? onPressed;
  final bool dense;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: KitThemeScope(
        brightness: Theme.of(context).brightness,
        child: FButton(
          key: Key('kit_button_${part.buttonId}'),
          size: dense ? FButtonSizeVariant.sm : FButtonSizeVariant.md,
          mainAxisSize: MainAxisSize.min,
          onPress: onPressed == null ? null : () => onPressed!(part),
          child: Text(part.label),
        ),
      ),
    );
  }
}
