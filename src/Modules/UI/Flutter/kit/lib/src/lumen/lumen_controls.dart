import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:gpt_markdown/gpt_markdown.dart';

import 'lumen_palette.dart';

/// A quiet solid surface, with one restrained shadow instead of nested blur.
final class LumenSurface extends StatelessWidget {
  const LumenSurface({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.radius = 22,
    this.selected = false,
    this.elevated = true,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final bool selected;
  final bool elevated;

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: LumenPalette.surface,
      borderRadius: BorderRadius.circular(radius),
      border: Border.all(
        color: selected ? LumenPalette.accent : LumenPalette.line,
        width: selected ? 1.5 : 1,
      ),
      boxShadow: elevated
          ? const [
              BoxShadow(
                color: Color(0x10203C29),
                blurRadius: 32,
                offset: Offset(0, 10),
              ),
            ]
          : null,
    ),
    child: Padding(padding: padding, child: child),
  );
}

/// The product's icon action; Forui owns focus, keyboard and disabled behavior.
final class LumenIconButton extends StatelessWidget {
  const LumenIconButton({
    super.key,
    required this.icon,
    required this.label,
    this.onPressed,
    this.selected = false,
    this.primary = false,
  });

  final Widget icon;
  final String label;
  final VoidCallback? onPressed;
  final bool selected;
  final bool primary;

  @override
  Widget build(BuildContext context) => Tooltip(
    message: label,
    child: FButton.icon(
      onPress: onPressed,
      variant: primary
          ? FButtonVariant.primary
          : selected
          ? FButtonVariant.secondary
          : FButtonVariant.outline,
      selected: selected,
      semanticsLabel: label,
      child: icon,
    ),
  );
}

/// A reusable input with caller-owned draft and focus lifecycle.
final class LumenTextField extends StatelessWidget {
  const LumenTextField({
    super.key,
    required this.controller,
    this.focusNode,
    this.hint,
    this.onChanged,
    this.onSubmitted,
    this.minLines = 1,
    this.maxLines = 5,
    this.enabled = true,
    this.autofocus = false,
    this.textInputAction = TextInputAction.send,
  });

  final TextEditingController controller;
  final FocusNode? focusNode;
  final String? hint;
  final ValueChanged<String>? onChanged;
  final ValueChanged<String>? onSubmitted;
  final int minLines;
  final int maxLines;
  final bool enabled;
  final bool autofocus;
  final TextInputAction textInputAction;

  @override
  Widget build(BuildContext context) => FTextField(
    control: FTextFieldControl.managed(
      controller: controller,
      onChange: (value) => onChanged?.call(value.text),
    ),
    focusNode: focusNode,
    hint: hint,
    minLines: minLines,
    maxLines: maxLines,
    enabled: enabled,
    autofocus: autofocus,
    textInputAction: textInputAction,
    onSubmit: onSubmitted,
  );
}

/// A labeled action backed by the same Forui control as icon actions.
final class LumenActionButton extends StatelessWidget {
  const LumenActionButton({
    super.key,
    required this.label,
    this.icon,
    this.onPressed,
    this.primary = false,
  });

  final String label;
  final Widget? icon;
  final VoidCallback? onPressed;
  final bool primary;

  @override
  Widget build(BuildContext context) => FButton(
    onPress: onPressed,
    variant: primary ? FButtonVariant.primary : FButtonVariant.outline,
    mainAxisSize: MainAxisSize.min,
    prefix: icon,
    child: Flexible(child: Text(label, textAlign: TextAlign.center)),
  );
}

/// Shared markdown rendering for the compact reply and other product surfaces.
final class KitMarkdown extends StatelessWidget {
  const KitMarkdown(this.text, {super.key, this.style});

  final String text;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) => GptMarkdown(
    text,
    style:
        style ?? Theme.of(context).textTheme.bodyMedium?.copyWith(height: 1.5),
    textDirection: Directionality.of(context),
  );
}
