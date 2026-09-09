import 'package:flutter/material.dart';

/// Approved Lumen design tokens. Legacy KitPalette remains dark until each
/// older surface is migrated, avoiding implicit color changes in charts.
abstract final class LumenPalette {
  static const background = Color(0xFFF7F7F2);
  static const surface = Color(0xFFFFFFFF);
  static const surfaceMuted = Color(0xFFF0F3EC);
  static const ink = Color(0xFF263934);
  static const muted = Color(0xFF687970);
  static const faint = Color(0xFF88958D);
  static const line = Color(0xFFDBE2DA);
  static const lineStrong = Color(0xFFB9CBC0);
  static const accent = Color(0xFF397B63);
  static const accentSoft = Color(0xFFE4EEE5);
  static const link = Color(0xFF8BAFA3);
  static const learned = Color(0xFF96A8C2);
  static const error = Color(0xFFAD4F3F);
  static const warning = Color(0xFF986A37);
}
