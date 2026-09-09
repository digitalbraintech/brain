import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:forui/forui.dart';
import 'package:material_ui/material_ui.dart' as material;

import '../lumen/lumen_palette.dart';

/// Product color tokens. Shell and kit both consume these — do not re-declare
/// palettes in the shell package.
abstract final class KitPalette {
  static const navigation = Color(0xFF101219);
  static const surface = Color(0xFF141720);
  static const surfaceRaised = Color(0xFF1B1F2A);
  static const surfaceSunken = Color(0xFF11141B);
  static const line = Color(0xFF292E3B);
  static const lineStrong = Color(0xFF363C4C);
  static const textPrimary = Color(0xFFE9EBF2);
  static const textMuted = Color(0xFF969CAF);
  static const textFaint = Color(0xFF5E6474);
  static const signal = Color(0xFFE09261);
  static const owner = Color(0xFF7B9BE3);
  static const success = Color(0xFF65C5A0);
}

abstract final class KitType {
  static const monoFamily = 'Cascadia Mono';
  static const monoFallback = ['Consolas', 'Menlo', 'monospace'];

  static const bodyFamily = 'Segoe UI Variable Text';
  static const bodyFallback = ['Segoe UI', 'Inter', 'Roboto'];

  static const title = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
    color: KitPalette.textPrimary,
  );

  static const heading = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 28,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.8,
    color: KitPalette.textPrimary,
  );

  static const cardTitle = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.15,
    color: KitPalette.textPrimary,
  );

  static const metric = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 17,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
    color: KitPalette.textPrimary,
  );

  static const body = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 15,
    height: 1.55,
    color: KitPalette.textPrimary,
  );

  static const bodyMuted = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 14,
    height: 1.5,
    color: KitPalette.textMuted,
  );

  static const meta = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w500,
    letterSpacing: 0.45,
    color: KitPalette.textMuted,
  );

  static const metaStrong = TextStyle(
    fontFamily: monoFamily,
    fontFamilyFallback: monoFallback,
    fontSize: 11,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.45,
    color: KitPalette.textPrimary,
  );

  static const empty = TextStyle(
    fontFamily: bodyFamily,
    fontFamilyFallback: bodyFallback,
    fontSize: 18,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.3,
    color: KitPalette.textPrimary,
  );
}

abstract final class KitTheme {
  // Forui + Lumen were selected for the product redesign. Keep the third-party
  // theme behind this bridge while legacy dark components migrate explicitly.
  static final _lumenForui = FThemeData(
    debugLabel: 'DigitalBrain Lumen',
    touch: true,
    colors: FColors.neutralLight.copyWith(
      background: LumenPalette.background,
      foreground: LumenPalette.ink,
      primary: LumenPalette.accent,
      primaryForeground: LumenPalette.surface,
      secondary: LumenPalette.accentSoft,
      secondaryForeground: LumenPalette.accent,
      muted: LumenPalette.surfaceMuted,
      mutedForeground: LumenPalette.muted,
      card: LumenPalette.surface,
      border: LumenPalette.line,
      destructive: LumenPalette.error,
      error: LumenPalette.error,
    ),
  );

  static ThemeData light() => ThemeData(
    useMaterial3: true,
    brightness: Brightness.light,
    fontFamily: KitType.bodyFamily,
    fontFamilyFallback: KitType.bodyFallback,
    colorScheme: const ColorScheme.light(
      primary: LumenPalette.accent,
      onPrimary: LumenPalette.surface,
      secondary: LumenPalette.accentSoft,
      onSecondary: LumenPalette.ink,
      surface: LumenPalette.surface,
      onSurface: LumenPalette.ink,
      error: LumenPalette.error,
      outline: LumenPalette.lineStrong,
      outlineVariant: LumenPalette.line,
    ),
    scaffoldBackgroundColor: LumenPalette.background,
    dividerColor: LumenPalette.line,
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: LumenPalette.surface,
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(12),
        borderSide: const BorderSide(color: LumenPalette.line),
      ),
    ),
    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: LumenPalette.ink,
        borderRadius: BorderRadius.circular(8),
      ),
      textStyle: const TextStyle(color: LumenPalette.surface, fontSize: 12),
    ),
  );

  static ThemeData dark() {
    final scheme = ColorScheme.fromSeed(
      seedColor: KitPalette.signal,
      brightness: Brightness.dark,
      surface: KitPalette.surface,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      scaffoldBackgroundColor: KitPalette.surface,
      colorScheme: scheme.copyWith(
        primary: KitPalette.signal,
        secondary: KitPalette.owner,
        surface: KitPalette.surface,
      ),
      dividerColor: KitPalette.line,
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: KitPalette.navigation,
        indicatorColor: KitPalette.signal.withValues(alpha: 0.14),
        selectedIconTheme: const IconThemeData(
          color: KitPalette.signal,
          size: 21,
        ),
        unselectedIconTheme: const IconThemeData(
          color: KitPalette.textMuted,
          size: 20,
        ),
        selectedLabelTextStyle: KitType.metaStrong.copyWith(
          color: KitPalette.signal,
        ),
        unselectedLabelTextStyle: KitType.meta,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: KitPalette.navigation,
        indicatorColor: KitPalette.signal.withValues(alpha: 0.14),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? KitType.metaStrong.copyWith(color: KitPalette.signal)
              : KitType.meta,
        ),
      ),
      tooltipTheme: const TooltipThemeData(
        decoration: BoxDecoration(color: KitPalette.surfaceRaised),
        textStyle: KitType.meta,
      ),
    );
  }
}

/// Installs the approved product control foundation below MaterialApp.
///
/// Localization stays in this bridge so surfaces never need to import Forui.
final class KitThemeScope extends StatelessWidget {
  const KitThemeScope({
    super.key,
    required this.child,
    this.brightness = Brightness.light,
  });

  final Widget child;
  final Brightness brightness;

  @override
  Widget build(BuildContext context) {
    final data = brightness == Brightness.light
        ? KitTheme._lumenForui
        : FTheme.neutral.dark.touch;
    return Localizations.override(
      context: context,
      delegates: FLocalizations.localizationsDelegates,
      child: material.Theme(
        data: data.toApproximateMaterialTheme(),
        child: material.Material(
          type: material.MaterialType.transparency,
          child: FTheme(data: data, child: child),
        ),
      ),
    );
  }
}

abstract final class KitChatTheme {
  static ChatTheme light() => ChatTheme(
    colors: const ChatColors(
      primary: LumenPalette.accent,
      onPrimary: LumenPalette.surface,
      surface: LumenPalette.background,
      onSurface: LumenPalette.ink,
      surfaceContainer: LumenPalette.surface,
      surfaceContainerLow: LumenPalette.surfaceMuted,
      surfaceContainerHigh: LumenPalette.accentSoft,
    ),
    typography: ChatTypography.standard(fontFamily: KitType.bodyFamily)
        .copyWith(
          bodyMedium: KitType.body.copyWith(color: LumenPalette.ink),
          bodySmall: KitType.bodyMuted.copyWith(color: LumenPalette.muted),
          labelSmall: KitType.meta.copyWith(color: LumenPalette.muted),
        ),
    shape: BorderRadius.circular(16),
  );

  static ChatTheme dark() {
    final base = ChatTypography.standard(fontFamily: KitType.bodyFamily);
    return ChatTheme(
      colors: const ChatColors(
        primary: KitPalette.owner,
        onPrimary: KitPalette.surface,
        surface: KitPalette.surface,
        onSurface: KitPalette.textPrimary,
        surfaceContainer: KitPalette.surfaceRaised,
        surfaceContainerLow: KitPalette.surfaceSunken,
        surfaceContainerHigh: KitPalette.line,
      ),
      typography: base.copyWith(
        bodyMedium: KitType.body,
        bodySmall: KitType.bodyMuted,
        labelSmall: KitType.meta,
      ),
      shape: BorderRadius.circular(14),
    );
  }
}
