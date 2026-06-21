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
  Given a marketplace neuron "market1"
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
