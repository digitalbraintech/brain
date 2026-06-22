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

Scenario: Tier-2 deploy with passing verify-build requests a restart
  Given a code deploy neuron "deploy1" with verify-build succeeding
  When I deploy module "GreeterNeuron" with source "// greeter"
  Then the timeline contains a CodeBuilt
  And the timeline contains a SiloRestartRequested

Scenario: Tier-2 deploy with failing verify-build rolls back without restart
  Given a code deploy neuron "deploy2" with verify-build failing
  When I deploy module "BadNeuron" with source "// broken"
  Then the timeline contains a FoundryRolledBack
  And the timeline does not contain a SiloRestartRequested

Scenario: Foundry loop checkpoints then completes a Tier-1 request
  Given a foundry loop neuron "foundry1"
  When I submit a foundry request "trivial Run returning ok" for tier "Run"
  Then the timeline contains a FoundryCheckpointed
  And the timeline contains a FoundryCompleted
