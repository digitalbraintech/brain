# DigitalBrain Product Finalization Plan

Date: 2026-06-27

## Product Target

The default user experience should be:

1. Start the Aspire app.
2. Open one Flutter client.
3. Sign in with a local username/password account.
4. See kernel tasks.
5. See installed experiences.
6. Run an installed experience.
7. For the first concrete experience, connect Gmail, fetch the last 100 emails, analyze them with the local Ollama model, and render the result as a live chart surface.

The client remains a thin renderer. Product logic lives in neurons, synapses, packs, and the marketplace/control-plane boundary.

## Current Baseline

Working primitives:

- Aspire AppHost wires storage, Orleans kernel replicas, Ollama/qwen, MCP, and Flutter.
- Kernel has task synapses, marketplace publish/install synapses, signed pack trust checks, local marketplace mode, `GeneratedNeuron`, and collectible ALC pack embodiment.
- Flutter subscribes to live surfaces and sends UI actions back through `UiGateway`.
- Chart neuron can generate chart surfaces from JSON rows and can use the local chat client when configured.

Not product complete:

- No real username/password auth flow.
- No persisted user sessions/tokens.
- No per-user installed experience catalog.
- No Gmail OAuth or Gmail fetcher.
- Private marketplace service is still an in-memory skeleton.
- Default AppHost starts placeholder/diagnostic resources that are not required for the happy path.

## Implementation Order

### Milestone 0: Make Runtime Health Meaningful

Goal: `aspire start` should show only resources that are part of the default product path.

Tasks:

1. Remove placeholder integrations from default AppHost startup.
2. Gate Telegram behind `DIGITALBRAIN_ENABLE_TELEGRAM=true`.
3. Gate the standalone diagnostic gateway behind `DIGITALBRAIN_ENABLE_DIAGNOSTIC_GATEWAY=true`.
4. Keep kernel-hosted gRPC/surface gateway as the only default client API.
5. Verify `dotnet build`, focused tests, `flutter analyze`, and `aspire describe`.

### Milestone 1: Local Account Login

Goal: Flutter starts on a neuron-driven login surface and obtains a user session.

Tasks:

1. Add core auth synapses: `LoginRequest`, `LoginSucceeded`, `LoginFailed`, `LogoutRequest`, `UserSessionCreated`.
2. Add `IUserSessionNeuron` in kernel with dev-local credential storage.
3. Emit a login `UiSurface` when no session is active.
4. Extend `UiGatewayService` to dispatch login synapses.
5. Thread `UserId` into install/run/task actions.
6. Add tests for valid login, invalid login, and session-scoped UI.

Initial scope: local dev users only. No Google OAuth, no billing identity.

### Milestone 2: Tasks And Installed Experiences As Product Surfaces

Goal: after login, the shell shows a useful task manager and an installed experience launcher.

Tasks:

1. Add a task registry query surface, not just per-task journal derivation.
2. Add an installed-experience registry derived from installed pack journals and user id.
3. Emit stable app shell menu items from the registry.
4. Ensure all run/install/open actions include `UserId`.
5. Add tests for login -> tasks surface -> installed experiences surface.

### Milestone 3: First Real Experience - Gmail Analytics

Goal: one installable experience proves the full loop.

Tasks:

1. Add Gmail auth/connect synapses and a first-party `Gmail.Analytics` pack.
2. Implement OAuth device/browser flow for Gmail in a narrow local-dev form.
3. Fetch last 100 messages through a Gmail connector service.
4. Summarize/classify senders, domains, categories, and time distribution with local Ollama.
5. Emit `VisualizeDataRequest` and then a chart `UiSurface`.
6. Add a simulation mode with seeded emails so the product works without real Google credentials.
7. Add E2E coverage for the simulated path first, then live OAuth as optional manual validation.

### Milestone 4: Private Marketplace Control Plane

Goal: move users, auth, catalog, entitlements, security scan, and commissions out of the kernel.

Tasks:

1. Replace `RemoteMarketplaceClientStub` with HTTP/gRPC client.
2. Add marketplace auth endpoints and persistent catalog storage.
3. Add entitlement checks for install/run.
4. Add publish-time security scan and explicit capability approval.
5. Add pay-as-you-go metering after the free first-party experience works.

## Deletion/Cleanup Backlog

- Keep `Projects/` as archive/reference only; do not wire from it.
- Delete or quarantine static Flutter marketplace screen after the surface-driven path covers authoring.
- Keep `brain.cs` only if it remains a useful thin launcher; otherwise retire it in favor of the AppHost.
- Remove placeholder executable integrations from the default AppHost.
- Re-evaluate standalone `DigitalBrain.Gateway`; kernel already hosts the real gRPC/surface gateway.

## Definition Of Done For The Product MVP

- `aspire start` reaches healthy default resources.
- Flutter opens to a login surface.
- A local user can sign in.
- The home shell shows tasks and installed experiences.
- Clicking an installed experience runs a real embodied pack.
- The Gmail analytics simulated experience emits a chart surface.
- The live Gmail path is behind explicit OAuth setup and does not block local/offline demo.
