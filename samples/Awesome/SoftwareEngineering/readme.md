# Awesome Software Engineering

Demonstrates two teams using the NeuroOS neuron system (with local LLM support via Qwen) to create simple apps.

## Teams

- **Software10** (old soft / legacy): traditional rigid code generation. See Software10/
- **Software20** (new): neuro-aware, prefers local LLM for higher quality generation. See Software20/

## Test files
The executable specs are:
- DigitalBrain.Tests/Features/AwesomeSoftware10.feature
- DigitalBrain.Tests/Features/AwesomeSoftware20.feature

Both teams expose:
- Synapses: CreateSimpleApp, SimpleAppCreated
- Neurons: Software10TeamNeuron, Software20TeamNeuron (the latter tagged with [LLM<Qwen>])

Run `dotnet test --filter Awesome` to verify both teams can create simple apps.

When running full `aspire run` (with Ollama + qwen model downloaded), Software20 will use the real local LLM for app code.
