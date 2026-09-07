# Context-based communication and executable handlers

Status: approved by the user after review of the written specification. Consolidated from the approved grilling decisions Q1–Q26. Implementation plan: [Context Communication TDD](../plans/2026-09-07-context-communication-tdd.md). This document supersedes the application/port/text-trigger architecture in `2026-09-07-file-based-scripting-design.md`. It describes the target, not capabilities already implemented.

The first implementation milestone is the complete assistant-authored brainstorming experience in Flutter, proved through TDD. The product is in development: remove obsolete code, APIs and tests; do not build migration or compatibility layers. Preserve useful behavior guarantees, not old abstraction names.

## 1. Ubiquitous language

| Term | Meaning and lifetime |
| --- | --- |
| Message | Typed command, request or event payload. Domain data includes an explicit conversation destination when needed. |
| Definition | Named, reusable, versioned implementation of a handler contract. Script and compiled implementations obey the same contract. |
| Participant | Addressable recipient implementing one or more handler contracts. An agent is a participant, not a separate execution system. |
| Synapse/subscription | Explicit durable route from a source and message contract to a recipient. Many deliveries use one subscription. It is not an execution or context. |
| Delivery | An immutable message envelope addressed to a recipient, including message identity, initiating principal, correlation and causation. Redelivery preserves its identity. |
| Execution | Durable processing of one admitted input by a pinned handler definition. Owns progress, result, deadline, child work and lifecycle. |
| Context | The handler's authorized access to its current execution. A fresh worker facade can reconnect to the same logical execution after a crash. |
| Activity | Observable history/projection of execution facts. It does not schedule, cancel or own work. |
| Correlation | Groups related work across one interaction. It is neither an execution identity nor a reply destination. |
| Activation | One atomic switch of a validated, revision-pinned set of definitions, dependencies and wiring. It is an installation concern, not an object every script constructs. |

A subscription can carry simultaneous requests without sharing their execution state. Each logical invocation has its own execution; retries preserve that execution. Context is not an ambient bag of unrelated services.

## 2. One programming model

Keep typed `IHandle<T>` contracts and the existing typed request/reply relationship. Reuse `IAgent`, `AgentRequest` and `AgentReply` for research, proposal, criticism, synthesis and composed brainstorming. Do not invent workflow-stage request types merely to name roles.

Scripts and compiled neurons implement identical externally visible semantics. A script implements one typed handler; its definition declares its input and output contracts outside the executable body. A behavior may contain several definitions, but does not require an author-facing `Application` wrapper.

The runtime discovers contracts, authenticates, routes, persists, schedules and recovers. Ordinary script code owns text matching, extraction, branching and business decisions. Delete `OnUserMessageContaining`, `ApplicationTextTrigger`, kernel text matching, and application-specific operation/port abstractions that duplicate the common handler model.

## 3. Communication semantics

| Operation | What successful await guarantees |
| --- | --- |
| Publish an event | Event and delivery obligations for its recipients are durably recorded. Subscriber completion is not awaited. |
| Send a command | One addressed participant successfully handled it. This is not merely a queue acknowledgement. |
| Request a response | One addressed participant completed with the declared typed result. |
| Start independent work | Explicit operation returning an execution reference. Independence is never an accidental side effect of failing to await. |

Send/request failures propagate to their caller. Normal C# `await`, `Task.WhenAll` and `try/catch` express orchestration. No separate workflow language.

Each publication resolves recipients against one committed routing revision. Persisted obligations, not a later subscription lookup, govern retries. New subscriptions observe future events by default. Historical replay is explicit new work with its own execution identity and retained origin provenance.

Subscriptions route by source/recipient identity and contract. They do not contain text predicates. Remove weight, decay and frequency-based changes from foundational routing. Any future adaptive routing is an explicitly installed policy handler.

Subscription removal prevents future obligations; it does not erase admitted work. Registration and subscription changes use public management APIs shared by the assistant and other clients. An ordinary handler invocation does not reinstall its definition or wiring.

## 4. Context boundary and provenance

Introduce `IContext` in contracts as the author-facing execution boundary. Keep it independent of Orleans interfaces. A host-provided bootstrap connection resolves an authenticated, leased execution capability. An execution ID alone grants no authority. The transport and grain layout remain implementation details.

Context exposes typed input, execution identity, initiating identity, causal provenance, communication, execution-local durable operations and completion. Keep definition authoring, global configuration and task management out of its fundamental surface; those are explicit capabilities with their own contracts.

Preserve the original correlation through child requests, sends and publications. Every child has a distinct execution ID and each message has a distinct message ID; preserve immediate parent execution and causing message independently. Never replace correlation with operation ID at an application boundary. Domain destinations such as conversation identity remain explicit payload data.

Retries resume the same execution. A deliberately repeated request creates new execution and message identities even when its text is identical.

## 5. Completion, ownership and recovery

A top-level script completes through context; a compiled handler return maps to the same authoritative completion operation. Persist the result before making it visible to the caller. Repeating an identical completion is idempotent; a conflicting completion is rejected. Exit without required completion is a failure. Publishing an event is not completion.

Children belong to their parent by default. Cancellation propagates, and success cannot be recorded while required children remain unfinished. Expected recovery is ordinary handler code. Explicit detachment creates independently manageable work rather than hiding it inside the parent's lifecycle.

Known unavailable definitions fail admission immediately. Every admitted execution has a finite deadline and fenced worker ownership. Worker crashes allow recovery; exhausted recovery or deadline expiry yields durable terminal failure visible to caller and task manager. A disconnected HTTP observer does not cancel durable execution. Never wait forever for an unavailable artifact.

Restart the pinned script and replay completed context operations using their retained results. Unfinished operations retain their identities. Detect mismatched replay target/input and fail visibly. Concurrent operation identity cannot depend on completion order.

Proposed API detail for implementation review: give observable steps explicit stable keys, scoped to the execution (and explicit branch/item keys for repeated work). This makes replay of ordinary concurrent C# independent of scheduler timing. These keys identify operations, not new domain message types.

Observable effects and replay-sensitive reads must use context-managed capabilities to receive recovery guarantees. Ordinary C# remains available for pure computation and branching. Raw HTTP, filesystem writes, clock reads and randomness bypass those guarantees; compiler validation alone cannot prove their absence. Discovery examples and tests must teach and verify the supported path.

External exactly-once effects are not promised without provider idempotency support. Cancellation does not undo completed effects; compensation is explicit domain behavior. Failure types distinguish cancellation, deadline expiry, unavailable recipient, worker/recovery failure and contract/replay mismatch. Expected domain outcomes remain response data where appropriate.

## 6. State, revisions and task management

Execution state stores this invocation's progress and retained observations/results. Shared state belongs to explicitly addressed domain participants/entities, such as the owner's brainstorming preferences. It is not implicitly shared between contexts. Shared state access must provide explicit concurrency semantics and participate in operation replay.

Pin each root execution to its activation's complete dependency closure. Children inherit that closure. Editing and activating definitions affects new roots only. Retain old source/artifacts while executions require them. A normal deletion cannot silently invalidate admitted work.

The task manager reads executions and their child relationships, with Activity explaining their history. Actions target execution IDs:

- Resume/retry retains execution identity, revision closure and completed results. Cancellation is terminal; retry policy must explicitly define which failed executions are resumable.
- Run again creates a new execution with fresh external observations and an explicitly selected activation revision (current active by default).
- Cancel stops owned unfinished work; completed effects remain recorded.

Activity derives from authoritative execution facts. It must not independently infer a conflicting completion status or serve as the mutable execution store.

## 7. Agent definitions and tool capabilities

Named, versioned agent definitions contain instructions and explicit allowed tool capabilities. A different name alone does not create a different role. All roles keep `AgentRequest -> AgentReply`.

Researcher receives web-search capability. Proposer and critic receive evidence and normally require no tools. The main assistant receives authoring/management capabilities through DigitalBrain MCP. Do not recursively expose its own chat invocation as an authoring tool.

Explicit capability selection must prevent restricted agents from discovering or initializing unrelated owner-only tools. Scenario isolation must not fail merely because a global self-MCP source rejects the scenario principal. Carry the caller's authorized identity through allowed capabilities without substituting a fixed owner.

Use the existing Tavily integration as the first search capability. Credentials remain Aspire secret parameters with setup descriptions, never script or prompt literals. Missing credentials, quota or provider failure produce visible outcomes; do not fabricate evidence or silently change providers. The live journey must show an actual search tool call, not just a model claim.

## 8. Proposed authoring example

The following is a proposed surface, not code supported by today's SDK. Template generation must supply the exact SDK/module references required by selected contracts. There is no `brain.Application`, `OnUserMessageContaining` or subscription installation inside this handler.

Definition: `brainstorm`, script `brainstorm.cs`, input `AgentRequest`, output `AgentReply`. Its activation includes the researcher, proposer and critic definitions and their allowed capabilities.

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.AI;

await using var context = await Context.ConnectAsync(args);
var request = context.Input<AgentRequest>();
var researcher = context.Get<IAgent>("researcher");
var proposer = context.Get<IAgent>("proposer");
var critic = context.Get<IAgent>("critic");

var research = await context.RequestAsync("research", researcher,
    new AgentRequest($"Research this idea using web search. Return evidence and source URLs. Idea: {request.Text}"),
    context.CancellationToken);
var proposal = await context.RequestAsync("proposal", proposer,
    new AgentRequest($"Propose an approach. Idea: {request.Text}\nEvidence: {research.Text}"),
    context.CancellationToken);
var criticism = await context.RequestAsync("criticism", critic,
    new AgentRequest($"Challenge assumptions and evidence. Idea: {request.Text}\nEvidence: {research.Text}\nProposal: {proposal.Text}"),
    context.CancellationToken);
var revision = await context.RequestAsync("revision", proposer,
    new AgentRequest($"Return a revised proposal, disagreements, open questions and sources. Idea: {request.Text}\nEvidence: {research.Text}\nProposal: {proposal.Text}\nCriticism: {criticism.Text}"),
    context.CancellationToken);

await context.CompleteAsync(revision);
```

The chat router is a separate handler. Its ordinary C# logic reads `UserMessaged.Text`, finds `brainstorm:` using `OrdinalIgnoreCase`, trims the following text and handles an empty idea explicitly. Matching messages request the reusable brainstorm participant; unmatched messages request the general assistant. The router sends the resulting text to the originating conversation and completes its own handling. Runtime routing never interprets the phrase.

Observers can independently subscribe to chat events, but do not compete for response ownership. The router owns this decision. A button or another handler can request brainstorming without inventing chat-shaped input.

The exact context overloads, command-completion shape and conversation response contract must be finalized in the implementation plan and exposed by discovery; examples must become compile-tested before being advertised as supported APIs.

## 9. Authoring and activation through MCP

Discovery must provide exact contracts, required references, handler/context signatures, capability requirements and compile-tested examples. Module selection must produce a usable template. Do not rely on the model guessing APIs from CLR type names or JSON schemas.

The assistant may create definitions and wiring, compile, inspect diagnostics and make bounded repairs within the owner's request. It records the original instruction and independent expectations before implementation. Source edits cannot silently alter those expectations. Test reports identify exact source, activation/dependency, artifact and expectation revisions.

Validate the whole proposed installation without exposing partially updated routing. Activation requires the independent acceptance suite to pass for that exact revision set. Commit definitions and wiring as one activation. Saving files or passing compilation is not evidence of activation or successful runtime behavior.

Learning means persistent, inspectable changes to definitions, subscriptions or shared domain state. Chat history is interpretive context, not the authoritative behavior store. Assistant claims must link to saved revision, test and execution evidence.

## 10. TDD: first milestone is the whole experience

First write a failing acceptance test for the owner's request: create a reusable brainstorm behavior from chat, research with web search, propose, critique, revise, reply to the original conversation, and reuse it after restart.

The test drives the same command ingress Flutter uses and the assistant's real MCP authoring tools. Use production discovery, references, compiler, definition storage, atomic activation, subscriptions, delivery, context, worker supervision, journals and execution projection. Do not substitute direct kernel calls for the authoring journey.

Control only external model/search responses. A strict model fixture responds at the real model boundary with tool calls and stage outputs; it does not bypass MCP. A controlled HTTP search provider returns fixed evidence. This proves the mechanics under those responses, not arbitrary natural-language reasoning. A separate live Flutter run proves one real model/search interaction.

Acceptance sequence:

1. Submit the owner instruction through chat. Capture the original instruction and independent expectations before generated source.
2. Assistant discovers the supported API and templates, saves the router/brainstorm/agent definitions and wiring, validates, runs scenarios and activates the exact passing set.
3. Send a message containing `brainstorm:`. Assert ordinary router code selects brainstorming and suppresses the general assistant response.
4. Observe one actual search capability invocation. Verify returned evidence and URLs reach proposal, criticism and revision in order. Verify the final response reaches the original conversation exactly once in the exercised retry path.
5. Send an unmatched message and an empty idea. Verify the specified general-assistant and empty-input behavior without kernel string matching.
6. Restart between research completion and subsequent work. Verify completed research is reused, causal identity is retained and unfinished work completes.
7. Submit a second idea after restart without recreating definitions. Verify fresh execution and search observations.
8. Inspect task management and Activity: one coherent execution tree with distinct child IDs and consistent terminal outcomes.

Keep focused tests for concurrent requests and concurrent child replay, cancellation, deadline/unavailable worker, contract/replay mismatch, activation atomicity and revision pinning, subscription changes around publication, and scenario identity/tool isolation. Do not mirror every implementation method with a test. Reuse the actual authored files across integration checks.

Scenarios need controlled external providers scoped to the scenario identity. They must exercise real production behavior while avoiding live credentials, owner-state contamination, authoring recursion and self-MCP owner rejection. Exact expected prose is appropriate for controlled fixtures; live-model checks assert observable tool use, evidence propagation and routing rather than one exact paragraph.

Live acceptance: create the behavior in Flutter, inspect saved code and test results, invoke a brainstorm with real search, inspect execution evidence, restart, and invoke another idea. Record limitations honestly. Do not claim this milestone complete from an isolated compiled script test.

## 11. Removal and verification criteria

Delete superseded text triggers, competing application routing/execution facades, implicit lifecycle installers inside handlers, weighted foundational routes, duplicated authoring catalogs and tests that only preserve those APIs. Consolidate causal identity and execution ownership rather than retaining adapters between competing models.

Retain or adapt useful tests for isolation, deduplication, persistence, causal propagation, worker fencing, cancellation and provider failure. No data migration is required. Destructive development-store cleanup, if needed, must target verified task-owned namespaces; no broad filesystem deletion.

Done means the complete first milestone passes, supported authoring examples compile against discovery-provided references, the live Flutter journey is recorded, terminal failures are observable, and obsolete paths are actually removed. It does not mean all possible authored programs or external side effects are guaranteed correct.

## 12. Decision trace and review boundaries

Q1–Q3: execution context lifetime, script-as-handler, ordinary router. Q4–Q8: shared contracts, communication guarantees, provenance, child ownership and Activity projection. Q9–Q14: one handler per script, authenticated bootstrap, state separation, revision retention, explicit subscriptions and full-experience-first TDD. Q15–Q20: completion, chat reply ownership, configured agents/tools, atomic activation, finite admission/recovery and bounded author repair. Q21–Q26: explicit synapses, replay, ordinary C# concurrency, retry versus run-again, routing snapshots and persistent learning.

Proposed API spelling and explicit operation keys in this document are design proposals for review, not evidence of an existing implementation. Storage/grain partitioning, finite timeout/retry defaults and exact management endpoint shapes belong in the implementation plan, constrained by these semantics. Do not silently restore an application-specific model to make implementation easier.
