# Awesome Software Engineering

Demonstrates the NeuroOS neuron system (with local LLM support via Qwen) creating a simple app.

## Team

- **Software20**: neuro-aware, prefers local LLM for higher quality generation. See Software20/

## Test files
The executable spec is:
- DigitalBrain.Tests/Features/AwesomeSoftware20.feature

Exposes:
- Synapses: CreateSimpleApp, SimpleAppCreated
- Neurons: Software20TeamNeuron (tagged with [LLM<Qwen>])

Run `dotnet test --filter Awesome` to verify the team can create simple apps.

When running full `aspire run` (with Ollama + qwen model downloaded), Software20 will use the real local LLM for app code.
