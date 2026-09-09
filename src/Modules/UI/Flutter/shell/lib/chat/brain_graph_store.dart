import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

/// Source-confirmed graph snapshots. Reconnect starts from a fresh snapshot;
/// retained history never pulses. Journals are observations, never work queues.
final class BrainGraphStore extends ChangeNotifier {
  BrainGraphStore({
    required this.read,
    this.watch,
    this.setSubscription,
    this.interval = const Duration(seconds: 2),
  }) {
    if (watch == null) {
      unawaited(refresh());
    } else {
      _connect();
    }
  }
  final ReadBrain? read;
  final WatchBrain? watch;
  final SetBrainSubscription? setSubscription;

  /// Retry delay only. There is no periodic graph polling.
  final Duration interval;
  BrainSnapshot? snapshot;
  String? failure;
  bool loading = false, mutating = false, stale = false;
  Set<String> activeNodes = {}, activeEdges = {};
  int revision = 0;
  bool _disposed = false, _reconnecting = false;
  Timer? _retry;
  StreamSubscription<BrainSnapshot>? _stream;
  Completer<void>? _readDone;

  void _connect() {
    if (_disposed || watch == null) return;
    _retry?.cancel();
    _stream = watch!().listen(
      (next) {
        if (_disposed) return;
        _apply(next, quiet: _reconnecting);
        _reconnecting = false;
      },
      onError: (Object error) => _disconnected(),
      onDone: _disconnected,
    );
  }

  void _disconnected() {
    if (_disposed || _retry?.isActive == true) return;
    _reconnecting = true;
    failure = 'Reconnecting. Showing the last observation.';
    stale = true;
    activeNodes = {};
    activeEdges = {};
    notifyListeners();
    unawaited(_stream?.cancel());
    _retry = Timer(interval, _connect);
  }

  void _apply(BrainSnapshot next, {bool quiet = false}) {
    final previous = quiet ? null : snapshot;
    activeNodes = {};
    activeEdges = {};
    if (previous != null) {
      final events = previous.activity.map((event) => event.id).toSet();
      final knownNodes = {for (final node in previous.nodes) node.id: node};
      activeNodes = next.activity
          .where((event) {
            if (event.kind == 'historical' || events.contains(event.id)) {
              return false;
            }
            final known = knownNodes[event.neuronId];
            if (known == null) {
              return event.timestamp?.isAfter(previous.observedAt) ?? false;
            }
            final cursor = event.direction.toLowerCase() == 'incoming'
                ? known.incomingSequence
                : known.outgoingSequence;
            return event.sequence > cursor;
          })
          .map((event) => event.neuronId)
          .toSet();
      final counts = {
        for (final edge in previous.synapses) edge.id: edge.fireCount,
      };
      activeEdges = next.synapses
          .where(
            (edge) =>
                counts.containsKey(edge.id) &&
                edge.fireCount > counts[edge.id]!,
          )
          .map((edge) => edge.id)
          .toSet();
    }
    snapshot = next;
    failure = null;
    stale = false;
    revision++;
    notifyListeners();
  }

  Future<void> refresh() async {
    if (_disposed || loading || read == null) return;
    loading = true;
    _readDone = Completer<void>();
    try {
      final next = await read!().timeout(const Duration(seconds: 12));
      if (!_disposed) _apply(next);
    } catch (_) {
      if (!_disposed) {
        failure = 'Cannot refresh the brain. Showing the last observation.';
        stale = true;
        activeNodes = {};
        activeEdges = {};
        notifyListeners();
      }
    } finally {
      loading = false;
      _readDone?.complete();
      _readDone = null;
    }
  }

  Future<bool> subscribe({
    required String sourceId,
    required String targetId,
    required String signalType,
    required bool subscribed,
  }) async {
    if (_disposed || mutating || read == null || setSubscription == null) {
      return false;
    }
    mutating = true;
    notifyListeners();
    try {
      await setSubscription!(
        sourceId: sourceId,
        targetId: targetId,
        signalType: signalType,
        subscribed: subscribed,
      ).timeout(const Duration(seconds: 15));
      if (_disposed) return false;
      await _readDone?.future;
      if (_disposed) return false;
      await refresh();
      if (failure != null) return false;
      final present =
          snapshot?.synapses.any(
            (edge) =>
                edge.sourceId == sourceId &&
                edge.targetId == targetId &&
                edge.signalType == signalType &&
                (!subscribed || edge.kind == 'Bound'),
          ) ??
          false;
      if (present != subscribed) {
        failure =
            'The subscription has not been confirmed. Refresh and try again.';
        return false;
      }
      return true;
    } catch (_) {
      if (!_disposed) {
        failure =
            'The subscription could not be changed. Refresh and try again.';
      }
      return false;
    } finally {
      mutating = false;
      if (!_disposed) notifyListeners();
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _retry?.cancel();
    unawaited(_stream?.cancel());
    super.dispose();
  }
}
