Feature: Runtime Code Foundry

Scenario: Code generation produces a CodeGenerated synapse
  Given a code gen neuron "codegen1"
  When I request generation of "a trivial Run method returning 42" for tier "Run"
  Then the timeline contains a CodeGenerated

Scenario: Tier-1 run executes generated logic and reports success
  Given a code run neuron "coderun1"
  When I run generated source returning text "tier1-ok"
  Then the timeline contains a CodeRunResult
  And the last CodeRunResult is successful with output containing "tier1-ok"

Scenario: Tier-1 run reports compile failure
  Given a code run neuron "coderun2"
  When I run invalid generated source
  Then the last CodeRunResult is a failure
