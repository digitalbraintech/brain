# Programmable activity home

**Goal:** Make the default brain compose its activity feed and split UI through startup scripts, with real causal activity selection and behavior editing.

**Architecture:** DigitalBrainActivated runs start.cs. It loads activities.cs and ui.cs from a versioned immutable source bundle. Activities subscribe to an execution-source neuron; its durable outbox drains on a separate interleaved turn so observers can call back into producers. A renderer subscribes to activities and persists a component tree and visible activity state. Flutter renders these primitives and observes the renderer. Selected results are projected from actual causal journals, including messages and card references. Runtime observations preserve owner, principal, correlation and causation without observing themselves.

**Tech stack:** .NET/Orleans, Roslyn C# scripting, HTTP/SSE and MCP, Flutter/Forui.

**Spec:** User's approved instructions in this task, September 6, 2026. Graph on the left, rich input on the right; graph-contained task manager; selected/all activity scopes; shared behavior debugger/editor; voice input; Settings contains UI Kit and OldUI.

**Global constraints:** Preserve existing user data and palette. No conversation navigation. Distinguish observed execution from proven completion. Protect principal data. UI subscription must actually control updates. No external communications during tests.

## Tasks

1. Add failing tests for script composition, dependency hashing and activation input; implement immutable script bundles and two-script startup.
2. Add activity contracts, bounded state reducer, execution facts and owner/principal filtering; test causality and telemetry-loop suppression.
3. Add surface component contracts, renderer activity handling, snapshot/stream projections and scripted scene creation; retire hardcoded boot composition.
4. Render the programmed home in Flutter. Connect activity selection, causal graph, rich chat, voice and the behavior editor. Move legacy routes and component gallery into Settings.
5. Run targeted .NET/Flutter suites; fix integration failures. Start the Aspire app, verify through real digitalbrain MCP, and exercise the native UI with computer use.

## Verification

Tests cover two independent message activities, inherited causal chains, principal isolation, selected versus all graph scope, subscription-driven UI updates, script dependency changes, and behavior revision pinning. Live checks cover startup receipts, synapses, activity changes and a harmless test behavior, plus UI selection and editing.

## Integration findings

- The first live startup exposed an awaited cycle when the renderer observed its own OpenSurface execution. Durable deferred activity delivery fixes this without excluding UI operations from the activity model.
- MCP had a duplicate ingress implementation retaining workspace-wide correlation; it now uses the same input micro-API as the native UI.
- Legacy chat journal watches do not contain the new Composer/Assistant path. Selected activity results now have their own principal-filtered journal projection, stable event IDs, and reconnect loading.
- The UI Kit is a searchable catalog of 16 component entries and 43 supported states; legacy kit colors are shown as existing component behavior.
- Surface streams use `surface-opened` wire names. The client accepts these and the legacy scene names, reconnects with bounded backoff, and rejects stale scene reads. Shared scene notifications never advance the principal journal cursor.

## Verified implementation

- Scripting: 35 tests passed; substrate/runtime: 130 tests passed.
- Selected E2E/projection tests: 34 passed, including the real MCP transport and two completed independent activities.
- Flutter: full shell suite 69 passed and full core suite 63 passed before the final stream changes. Final stream/race changes passed 36 scoped core tests and 9 Home tests. Analyzers are clean.
- Gallery: 5 tests passed, exercising all 43 preview states at narrow and wide sizes.
- Live Aspire startup succeeded with `execution → activities → desk` subscriptions. Actual MCP messages produced separate completed activities and retrievable user/reply pairs. A test behavior was saved, enabled, invoked, observed through its journal, and left disabled.
- Local Whisper transcribed the public [whisper.cpp JFK sample](https://github.com/ggml-org/whisper.cpp/blob/master/samples/jfk.wav) through the voice endpoint and created a completed activity with retained results.
- Native Home rendered and hot restart succeeded with no runtime errors. Desktop input automation could not focus its window; browser interaction checks use the same app and backend through a local same-origin proxy.
- Browser interaction checks passed: activity selection loads its graph and stored output; All combines activities and selecting another activity returns to causal scope; the graph inspector opens the behavior editor; editing and saving creates a valid draft without replacing the active revision; gallery search and error states work; OldUI opens and returns to Home. A message sent from Home automatically selected its new activity, reached completed, and displayed its answer. Browser console has no warnings or errors.
