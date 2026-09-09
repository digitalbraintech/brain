import 'package:flutter/material.dart';

import '../components/graph/graph_models.dart';

final class OnboardingLessonFrame {
  const OnboardingLessonFrame({
    required this.nodes,
    required this.edges,
    this.pulse,
    this.highlightEdgeId,
    this.duration = const Duration(milliseconds: 1100),
  });

  final List<GraphNode> nodes;
  final List<GraphEdge> edges;
  final GraphPulse? pulse;
  final String? highlightEdgeId;
  final Duration duration;
}

final class OnboardingCapability {
  const OnboardingCapability({
    required this.id,
    required this.title,
    required this.blurb,
    required this.rule,
    required this.icon,
    required this.frames,
  });

  final String id;
  final String title;
  final String blurb;
  final String rule;
  final IconData icon;
  final List<OnboardingLessonFrame> frames;
}
