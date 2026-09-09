import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/material.dart';

import '../brain_theme.dart';
import 'credential_store_io.dart'
    if (dart.library.html) 'credential_store_web.dart'
    as store;
import 'login_screen.dart';

/// Builds the signed-in shell once a client is available.
typedef ShellBuilder = Widget Function(
  DigitalBrainUiClient? client,
  String? statusMessage,
);

/// Creates a client for the given credentials, or throws if the kernel
/// location cannot be resolved.
typedef ClientFactory = DigitalBrainUiClient Function(
  BasicCredentials? credentials,
);

DigitalBrainUiClient _fromEnvironment(BasicCredentials? credentials) =>
    DigitalBrainUiClient.fromEnvironment(credentials: credentials);

/// Stands between app start and the shell, prompting for credentials only when
/// the kernel actually demands them.
///
/// An explicit 401 is the sole trigger for the login screen. An unreachable or
/// ungated kernel falls through to the shell exactly as before, so local dev,
/// Aspire runs, and the desktop build never see a prompt.
final class BrainSessionGate extends StatefulWidget {
  const BrainSessionGate({
    super.key,
    required this.builder,
    this.createClient = _fromEnvironment,
  });

  final ShellBuilder builder;
  final ClientFactory createClient;

  @override
  State<BrainSessionGate> createState() => _BrainSessionGateState();
}

enum _Phase { probing, login, ready }

final class _BrainSessionGateState extends State<BrainSessionGate> {
  _Phase _phase = _Phase.probing;
  DigitalBrainUiClient? _client;
  String? _statusMessage;
  String? _loginError;
  String _lastUsername = '';

  @override
  void initState() {
    super.initState();
    final stored = store.readStoredCredentials();
    _lastUsername = stored?.username ?? '';
    unawaited(_attempt(stored, fromStartup: true));
  }

  /// Returns an error message when the credentials were rejected, else null.
  Future<String?> _attempt(
    BasicCredentials? credentials, {
    bool fromStartup = false,
  }) async {
    final DigitalBrainUiClient client;
    try {
      client = widget.createClient(credentials);
    } on Object catch (error) {
      // No resolvable kernel URL: surface it in the shell as before.
      debugPrint('DigitalBrain session failed: $error');
      _enterShell(null, error.toString());
      return null;
    }

    bool accepted;
    try {
      accepted = await client.checkAuth();
    } on Object catch (error) {
      // Unreachable kernel is not a credential failure. Let the shell open and
      // report it, which is what it did before a gate existed.
      debugPrint('DigitalBrain auth check failed: $error');
      _enterShell(client, error.toString());
      return null;
    }

    if (!accepted) {
      client.close();
      if (credentials != null) {
        store.clearStoredCredentials();
      }
      if (mounted) {
        setState(() {
          _phase = _Phase.login;
          _loginError = fromStartup && credentials == null
              ? null
              : 'Sign in failed. Check the username and password.';
        });
      }
      return 'Sign in failed. Check the username and password.';
    }

    if (credentials != null) {
      store.writeStoredCredentials(credentials);
    }
    _enterShell(client, null);
    return null;
  }

  void _enterShell(DigitalBrainUiClient? client, String? statusMessage) {
    if (!mounted) {
      client?.close();
      return;
    }
    setState(() {
      _phase = _Phase.ready;
      _client = client;
      _statusMessage = statusMessage;
    });
  }

  Future<String?> _signIn(String username, String password) {
    _lastUsername = username;
    return _attempt(BasicCredentials(username: username, password: password));
  }

  @override
  Widget build(BuildContext context) {
    if (_phase == _Phase.ready) {
      return widget.builder(_client, _statusMessage);
    }

    return MaterialApp(
      title: 'DigitalBrain',
      debugShowCheckedModeBanner: false,
      theme: BrainTheme.dark(),
      home: _phase == _Phase.login
          ? LoginScreen(
              onSubmit: _signIn,
              initialUsername: _lastUsername,
              errorMessage: _loginError,
            )
          : const ColoredBox(
              color: BrainPalette.surface,
              child: Center(child: CircularProgressIndicator()),
            ),
    );
  }
}
