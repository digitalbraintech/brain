Feature: Runtime Code Foundry

Scenario: Code generation produces a CodeGenerated synapse
  Given a code gen neuron "codegen1"
  When I request generation of "a trivial Run method returning 42" for tier "Run"
  Then the timeline contains a CodeGenerated
