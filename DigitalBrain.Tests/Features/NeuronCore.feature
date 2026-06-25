Feature: DigitalBrain Neuron Core

Scenario: Sending a synapse journals it and is replayable
  Given a demo neuron "test-english"
  When I fire a DemoMessageSynapse with text "grok build"
  Then the timeline contains a DemoMessageSynapse
  And replaying shows the message

Scenario: Aspire neuron handles start and emits completion
  Given an aspire orchestrator neuron "aspire1"
  When I fire a StartDistributedApp for "my-brain-app"
  Then the timeline contains a DistributedAppStarted

Scenario: Marketplace publishes and lists packs
  Given a marketplace neuron "market-main"
  When I publish pack "EmailVisualizer" version "1.0"
  And I request published list
  Then the timeline contains a PublishedList

Scenario: Compiler meta-neuron generates code from English description
  Given a compiler neuron "compiler1"
  When I send create neuron request "analyze emails with chart output"
  Then the timeline contains a NeuronCodeGenerated

Scenario: Meta optimizer tracks telemetry and proposes wiring improvements
  Given a meta optimizer neuron "optimizer1"
  And a demo neuron "demo-opt"
  When I fire multiple messages to trigger telemetry
  Then the timeline contains a WiringOptimizationProposed

Scenario: Full grok create-neuron flow: create -> publish to marketplace -> download/install -> use
  Given a marketplace neuron "market-main"
  Given a compiler neuron "compiler-flow"
  When I send create neuron request "grok email analyzer chart"
  Then the timeline contains a NeuronCodeGenerated
  When I publish pack "Generated-grokemailanalyzerchart" version "0.1-dev"
  And I request published list
  Then the timeline contains a PublishedList
  When I download/install the pack "Generated-grokemailanalyzerchart" version "0.1-dev"
  Then the timeline contains a NeuroPackInstalled

Scenario: Simulate a causal scene with ordered synapse sequence and replay (Durable journal)
  Given a demo neuron "scene-demo"
  When I fire a DemoMessageSynapse with text "step-1"
  And I fire a DemoMessageSynapse with text "step-2"
  Then the timeline contains these synapse types in causal order: DemoMessageSynapse, DemoMessageSynapse
  And replaying shows the message

Scenario: Harness simulates other-brain publish-install-use flow via Marketplace contract
  Given a marketplace neuron "market-main"
  Given a compiler neuron "compiler-harness"
  When I send create neuron request "email analyzer for other brain"
  Then the timeline contains a NeuronCodeGenerated
  When I publish, a simulated other brain installs and uses the pack "Generated-emailanalyzerforotherbrain" version "0.1-sim"
  Then the timeline contains a PublishedList
  And the generated neuron for pack "Generated-emailanalyzerforotherbrain" received an ExperienceUsed

Scenario: System self-awareness with status, fix proposal and simulation
  Given a system status neuron "status-self"
  When I fire a bad status for component "kernel"
  Then the timeline contains a FixProposal
  And the timeline contains a SimulationResult with success true

Scenario: Kernel self-update publishes as pre-installed pack then requests rolling restart via replicas
  Given a marketplace neuron "market-kupdate"
  Given an aspire orchestrator neuron "aspire-kupdate"
  When I publish pack "kernel" version "rolling-2026.6"
  And I download/install the pack "kernel" version "rolling-2026.6"
  And I fire a StartDistributedApp for "silo"
  Then the timeline contains a NeuroPackInstalled
  And the timeline contains a DistributedAppStarted
