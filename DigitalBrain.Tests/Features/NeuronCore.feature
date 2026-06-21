Feature: NeuroOS Neuron Core

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
