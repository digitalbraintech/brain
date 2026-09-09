import 'dart:async';

import 'package:flutter/foundation.dart';

import 'onboarding_catalog.dart';
import 'onboarding_models.dart';

/// Advances lesson frames. Production graph widgets consume [frame].
final class OnboardingLessonPlayer extends ChangeNotifier {
  OnboardingLessonPlayer({
    List<OnboardingCapability>? catalog,
    this.animate = true,
  }) : catalog = catalog ?? OnboardingCatalog.capabilities {
    _capability = this.catalog.first;
    _frameIndex = animate ? 0 : _capability.frames.length - 1;
  }

  final List<OnboardingCapability> catalog;
  final bool animate;

  late OnboardingCapability _capability;
  int _frameIndex = 0;
  Timer? _timer;

  OnboardingCapability get capability => _capability;
  int get frameIndex => _frameIndex;
  OnboardingLessonFrame get frame => _capability.frames[_frameIndex];

  void select(String id) {
    _timer?.cancel();
    _capability = catalog.firstWhere((item) => item.id == id);
    _frameIndex = animate ? 0 : _capability.frames.length - 1;
    notifyListeners();
    _arm();
  }

  void replay() {
    _timer?.cancel();
    _frameIndex = animate ? 0 : _capability.frames.length - 1;
    notifyListeners();
    _arm();
  }

  void start() => _arm();

  void _arm() {
    if (!animate) {
      return;
    }
    if (_frameIndex >= _capability.frames.length - 1) {
      return;
    }
    _timer = Timer(_capability.frames[_frameIndex].duration, () {
      _frameIndex++;
      notifyListeners();
      _arm();
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }
}
