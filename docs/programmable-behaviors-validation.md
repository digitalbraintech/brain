# Programmable applications: validation record

7 September 2026. Implementation is in progress on `codex/day-zero-scripting`; changes are uncommitted.

The design and remaining work are recorded in [the specification](superpowers/specs/2026-09-07-file-based-scripting-design.md) and [implementation plan](superpowers/plans/2026-09-07-programmable-brain-tdd.md). Earlier totals from the removed behavior runtime do not verify this replacement.

## What the tests exercise

Tests use the production compiler, application kernel, explicit routing, journals and worker host. Real compiled-file tests cover publication and process supervision. External model and search responses are controlled; the application runtime is not replaced by a mock.

Observed passing focused checks include:

- Assistant template, save, validate, activate and invoke through the real process supervisor.
- Chat matching and principal isolation: eight cases.
- Reusable agent composition, including chat-triggered brainstorm and reply to the originating conversation: five cases.
- Revision-pinned child graphs, exactly one RunAsync boundary, explicit initialization and completion-only UI effects.
- Durable delay across restart, sequential delays, unrelated-scope progress and neuron event waiting across restart.
- UI ComponentAdded commit/retry recovery and authenticated button ingress: four HTTP cases.
- Tavily HTTP behavior and Aspire secret parameter wiring: seven cases. These use sentinel credentials, not a live search account.

The latest broad Simulation run passed **93/93**, including graph worker failure, recorded values, cancellation, parent/child cancellation across restart, and application-event delivery into neuron inputs. Substrate passed **11/11**. The solution currently builds with zero warnings and errors. Authoring passed **10/10**, AI passed **17/17**, and Flutter shell passed **9/9**; subsequent expectation-tool checks passed individually.

## Current work

Multi-file source bundles retain siblings, use one optimistic source revision, and preserve owner edits during startup. Assistant, HTTP, MCP and Flutter tests exercise the shared authoring path. Startup uses that same service and supervision path. The obsolete behavior execution contracts and consumers have been removed.

An initial Aspire smoke check saved a chat program, ran its scenario, activated it, and received one `pong` through `/owner/commands`. After a complete stop/restart, another `/ping` received `pong` without recreating the program. That check exposed old journal records referring to deleted grains; the matching v2 state/journal namespace configuration has now also been exercised live.

The subsequent MCP runtime check used `aspire start --isolated` in Testing mode and actual MCP initialize/tools/call requests to the discovered MCP endpoint. It discovered the catalog and template, saved `mcp-runtime-chat`, recorded an independent expectation, compiled it, passed its chat scenario and activated it. `/runtime-check` returns `Your saved behavior is running.` After a complete Aspire stop/start, MCP recovered the earlier reply, a new command returned the same expected text, and retrying that command left exactly one correlated Responded journal entry. The scenario report retained the same source, artifact and expectation revisions. No matching failure/obsolete-behavior errors appeared in the inspected final kernel logs. Aspire remains running for inspection.

This live check found and fixed two integration gaps: shared HTTP defaults omitted service discovery, and MCP chat observed the obsolete assistant-only reply journal. Regression tests first failed and then passed using a real HTTP listener and a real authored chat handler. The latest full Authoring run passed **12/12** (`.test-results/authoring-runtime-final.log`). The other suite totals above predate these two integration fixes. MCP protocol evidence is retained under `.test-results/mcp-*-result.json`; raw Aspire diagnostics are ignored and must not be published because they can contain credentials.

Full current backend regressions, final independent review and further browser checks remain required. Interactive secret onboarding has not been verified.

## Assistant self-MCP and immediate chat failures

The latest full Authoring run passed **19/19** (`.test-results/authoring-self-mcp-final4.log`), including real HTTP MCP authoring, missing-route errors, provider failure notification, shipped startup reapply, and startup routing with both connection-bound and ambient verified identities. AI passed **16/16** and focused Simulation chat/routing checks passed **10/10** before the final SDK identity correction. The final solution build passed with zero warnings/errors (`.test-results/build-self-mcp-final2.log`). These are distinct checks; older broad suite totals above are not fresh validation of every change.

In Development mode with fakes disabled, the actual UI command endpoint received “List my saved C# behaviors and briefly explain what each does. Use your tools.” The configured gpt-5.6-luna model replied in **11.48 seconds**, correctly explaining `start` and `mcp-runtime-chat`. Trace `1273f9660232a72485ade7d0d80cb356` records `execute_tool list_applications` and two `execute_tool read_application` calls through the discovered MCP tools. Evidence: `.test-results/self-mcp-ui-chat.sse`, `self-mcp-tool-traces.json`, and the compact `self-mcp-tool-summary.json`. This verifies real Aspire endpoint injection, DI, HTTP MCP discovery/invocation and model response through the UI backend; it is not a browser rendering check.

The retained `start` source was explicitly updated through MCP, validated and activated, preserving its sibling scripts. The installer still preserves saved owner edits. Runtime validation exposed typed ports ignoring the ambient verified principal; the regression failed before the SDK correction and passed afterward.

Rebuilding the SDK left an older saved application artifact unavailable. An already-admitted `/runtime-check` command waited instead of reporting worker unavailability. Revalidating the unchanged source, rerunning its unchanged acceptance expectation, and activating the rebuilt artifact succeeded. **Worker-unavailability admission/terminal handling remains a separate gap**; immediate missing-route and assistant/provider failures are covered, but do not imply all unavailable-worker cases terminate promptly. The accepted source and expectation hashes stayed unchanged; the new artifact is `7296ca31c93a262bc9dccbc560981ed59fb1e28b59127f008ef525d42715e3f5`.

The final restart temporarily encountered an unavailable Docker Linux engine; `docker desktop start` restored it. After restarting through Aspire, kernel, MCP and Flutter were Running. `/runtime-check` returned `Your saved behavior is running.` through `/owner/commands` (command `7fd53401e4814726ae65d3d0932e71f7`). `/hello` received an assistant response saying no saved behavior could be determined, rather than hanging (command `46b267001c4d44d59b9cd54dd3ba973f`). Evidence is retained in `.test-results/self-mcp-saved-behavior.sse` and `self-mcp-hello.sse`. The app is left running.

Deterministic tests establish behavior for the exercised inputs and recovery boundaries; they do not prove that a model will translate every new natural-language request correctly. Immutable expectation records, scenario execution, and assistant/MCP recording tools now work. Mandatory adoption across every authoring surface, complete user-intent provenance, multi-step/provider scenarios, limits, dynamic creation, graph consistency and retention remain unfinished.

## Fundamental remaining guarantees

1. Every authoring surface must enforce independent accepted expectations before activation, preserve unrelated intent and record its verified origin. Optional expectations still let an application activate without proving the user's instruction.
2. Dynamically created child applications need recoverable creation/validation/activation identities and bounded authority. This closes the gap between manually saved composition and reliable on-demand self-programming.
3. Wait deadlines, execution budgets and remaining lifecycle semantics must be explicit and durable. Parent/child command cancellation is implemented, but does not establish every agent/neuron cancellation boundary.
4. Dependency updates need atomic graph activation and complete source provenance, so recovery cannot expose a partially updated composition.
5. Multi-step chat/UI/provider scenarios must exercise the same external boundaries as the app. The successful MCP smoke proves one deterministic saved behavior, persistence and retry; it does not prove unseen natural-language translation, live Tavily, interactive credential onboarding or browser rendering.

SDK packaging and artifact retention are operational follow-through after those programming guarantees. Final review remains required.
